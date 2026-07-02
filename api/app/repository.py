"""
Normalize congress.gov JSON into the schema and read it back.

Parsing helpers are pure (unit-testable without a DB); the async functions do
the SQL. Upserts use ON CONFLICT so re-pulling a bill (?refresh=true) is
idempotent — the proof slice's INSERT OR REPLACE idiom, in Postgres form.
"""
from __future__ import annotations

import re
from datetime import datetime, timezone

from psycopg import AsyncConnection
from psycopg.rows import dict_row
from psycopg.types.json import Json


def bill_slug(congress: int, bill_type: str, bill_number: int) -> str:
    return f"{congress}-{bill_type.lower()}-{bill_number}"


def _version_code(name: str | None, url: str | None) -> str:
    """Short, stable code for a text version.

    Prefer the govinfo filename suffix (…119hr6644enr.htm -> 'enr'); otherwise
    slugify the version name. Never empty (it is part of a primary key).
    """
    m = re.search(r"BILLS-\d+[a-z]+\d+([a-z]+)\.", url or "")
    if m:
        return m.group(1)
    slug = re.sub(r"[^a-z0-9]+", "", (name or "").lower())[:20]
    return slug or "unknown"


def parse_bill(bill_json: dict) -> dict:
    """Extract normalized bill columns from a /bill response."""
    b = bill_json.get("bill", bill_json)
    la = b.get("latestAction") or {}
    return {
        "congress": b.get("congress"),
        "bill_type": (b.get("type") or "").lower(),
        "bill_number": int(b["number"]) if b.get("number") is not None else None,
        "title": b.get("title"),
        "origin_chamber": b.get("originChamber"),
        "latest_action_date": la.get("actionDate"),
        "latest_action_text": la.get("text"),
        "update_date": b.get("updateDate") or b.get("updateDateIncludingText"),
    }


def parse_text_versions(text_json: dict) -> list[dict]:
    """Flatten /bill/.../text into one dict per (version, format)."""
    out: list[dict] = []
    for v in text_json.get("textVersions", []) or []:
        name = v.get("type")
        vdate = v.get("date")
        for fmt in v.get("formats", []) or []:
            url = fmt.get("url")
            out.append({
                "version_code": _version_code(name, url),
                "version_name": name,
                "version_date": vdate,
                "format_type": fmt.get("type") or "unknown",
                "url": url,
            })
    return out


async def store_law(
    con: AsyncConnection,
    congress: int,
    bill_type: str,
    bill_number: int,
    bill_json: dict,
    text_json: dict,
    bodies: dict[str, str],
) -> str:
    """Upsert a bill + its text versions (+ raw payloads) in one transaction.

    `bodies` maps a text-version URL -> fetched body text. Returns the bill_id.
    """
    slug = bill_slug(congress, bill_type, bill_number)
    b = parse_bill(bill_json)
    vintage = datetime.now(timezone.utc)

    async with con.transaction():
        await con.execute(
            """
            INSERT INTO bills (bill_id, congress, bill_type, bill_number, title,
                               origin_chamber, latest_action_date, latest_action_text,
                               update_date, source_id, data_vintage)
            VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,'congress_gov',%s)
            ON CONFLICT (bill_id) DO UPDATE SET
                title = EXCLUDED.title,
                origin_chamber = EXCLUDED.origin_chamber,
                latest_action_date = EXCLUDED.latest_action_date,
                latest_action_text = EXCLUDED.latest_action_text,
                update_date = EXCLUDED.update_date,
                data_vintage = EXCLUDED.data_vintage
            """,
            (slug, congress, bill_type.lower(), bill_number, b["title"],
             b["origin_chamber"], b["latest_action_date"], b["latest_action_text"],
             b["update_date"], vintage),
        )

        # Replace text versions wholesale so stale rows never linger.
        await con.execute("DELETE FROM bill_text_versions WHERE bill_id = %s", (slug,))
        for v in parse_text_versions(text_json):
            await con.execute(
                """
                INSERT INTO bill_text_versions
                    (bill_id, version_code, version_name, version_date,
                     format_type, url, text_content)
                VALUES (%s,%s,%s,%s,%s,%s,%s)
                ON CONFLICT (bill_id, version_code, format_type) DO UPDATE SET
                    version_name = EXCLUDED.version_name,
                    version_date = EXCLUDED.version_date,
                    url = EXCLUDED.url,
                    text_content = EXCLUDED.text_content
                """,
                (slug, v["version_code"], v["version_name"], v["version_date"],
                 v["format_type"], v["url"], bodies.get(v["url"] or "")),
            )

        for endpoint, payload in (("bill", bill_json), ("text", text_json)):
            await con.execute(
                """
                INSERT INTO raw_payloads (bill_id, endpoint, fetched_at, http_status, payload_json)
                VALUES (%s,%s,%s,%s,%s)
                """,
                (slug, endpoint, vintage, 200, Json(payload)),
            )
    return slug


async def get_bill(con: AsyncConnection, bill_id: str) -> dict | None:
    """Assemble a stored bill + its text versions, or None if absent."""
    async with con.cursor(row_factory=dict_row) as cur:
        await cur.execute("SELECT * FROM bills WHERE bill_id = %s", (bill_id,))
        row = await cur.fetchone()
        if row is None:
            return None
        await cur.execute(
            "SELECT version_code, version_name, version_date, format_type, url, text_content "
            "FROM bill_text_versions WHERE bill_id = %s ORDER BY version_date, format_type",
            (bill_id,),
        )
        row["text_versions"] = await cur.fetchall()
    return row
