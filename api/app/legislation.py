"""
Legislation tracker: discover, filter, and maintain housing bills in Postgres.

congress.gov's list endpoint cannot filter by policy area (verified against
the v3 swagger — the only list parameters are congress/type/date/sort), so the
tracker corpus is maintained by sync:

  * sync_window  — walk /bill/{congress} newest-updates-first for a date
                   window, detail-fetch each changed bill, and keep those whose
                   policyArea is "Housing and Community Development".
  * sync_seed    — fetch one known bill plus its /relatedbills and keep the
                   housing ones. Cheap way to pull a cluster of relevant bills.

Both are bounded by the hard caps in config (sync_max_list_pages,
sync_max_detail_calls) so a single run stays far below the 5,000 req/hour
API limit. Rows land in the same `bills` table the retrieval endpoint uses,
plus `bill_sponsors` and `bill_summaries` (replaced wholesale per bill).
"""
from __future__ import annotations

import re
from datetime import datetime, timedelta, timezone

from psycopg import AsyncConnection
from psycopg.rows import dict_row
from psycopg.types.json import Json

from . import config
from .congress_client import BillNotFound, CongressClient
from .repository import bill_slug

REF_PREFIX = {
    "hr": "H.R.", "s": "S.", "hjres": "H.J.Res.", "sjres": "S.J.Res.",
    "hconres": "H.Con.Res.", "sconres": "S.Con.Res.", "hres": "H.Res.", "sres": "S.Res.",
}


def format_ref(bill_type: str, bill_number: int) -> str:
    return f"{REF_PREFIX.get(bill_type.lower(), bill_type.upper())} {bill_number}"


def congress_ordinal(congress: int) -> str:
    n = congress % 100
    suffix = "th" if 11 <= n <= 13 else {1: "st", 2: "nd", 3: "rd"}.get(congress % 10, "th")
    return f"{congress}{suffix}"


def status_key(latest_action_text: str | None) -> str:
    """Coarse pipeline stage for the tracker's status pill."""
    t = (latest_action_text or "").lower()
    if "became public law" in t or "became private law" in t:
        return "enacted"
    if "vetoed" in t and "overridden" not in t:
        return "failed"
    if "to the president" in t or "presented to president" in t:
        return "to_president"
    if ("passed" in t or "agreed to" in t or "calendar" in t
            or "reported" in t or "ordered to be reported" in t
            or "motion to reconsider" in t or "amendment" in t):
        return "advancing"
    if "committee" in t or "read twice" in t or "referred" in t or "hearing" in t:
        return "committee"
    return "introduced"


def parse_tracker_bill(bill_json: dict) -> dict:
    """Extract tracker columns + sponsors from a /bill detail response."""
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
        "introduced_date": b.get("introducedDate"),
        "policy_area": (b.get("policyArea") or {}).get("name"),
        "sponsors": [
            {
                "bioguide_id": s.get("bioguideId"),
                "full_name": s.get("fullName"),
                "first_name": s.get("firstName"),
                "last_name": s.get("lastName"),
                "party": s.get("party"),
                "state": s.get("state"),
                "district": s.get("district"),
                "is_by_request": s.get("isByRequest"),
                "url": s.get("url"),
            }
            for s in (b.get("sponsors") or [])
        ],
    }


def strip_html(text: str | None) -> str:
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", text or "")).strip()


# --- summary tagging --------------------------------------------------------
# Keyword taxonomy scanned against title + CRS summary when the summary is
# first ingested. Deterministic and local — no model calls. Short keywords
# (<= 5 chars) are matched on word boundaries so 'PHA' doesn't hit 'alpha'.
TAG_RULES: list[tuple[str, list[str]]] = [
    ("Zoning & Land Use", ["zoning", "land use", "land-use", "upzoning", "lot size",
                           "density", "permitting", "by-right", "entitlement"]),
    ("Housing Supply", ["housing supply", "housing production", "new construction",
                        "housing shortage", "supply of housing", "increase the supply",
                        "housing units", "infill"]),
    ("Affordable Housing", ["affordable housing", "affordability", "low-income housing",
                            "workforce housing", "moderate-income"]),
    ("Rent Regulation", ["rent control", "rent stabilization", "rent cap", "rent regulation"]),
    ("Tenant Protections", ["tenant", "eviction", "just cause", "renter"]),
    ("Public Housing", ["public housing", "housing agency", "pha"]),
    ("Vouchers & Rental Assistance", ["voucher", "section 8", "rental assistance",
                                      "housing choice"]),
    ("Homelessness", ["homeless", "emergency shelter", "supportive housing"]),
    ("Mortgage & Finance", ["mortgage", "fha", "loan", "lender", "underwriting",
                            "housing finance", "appraisal"]),
    ("Tax Credits & Incentives", ["tax credit", "lihtc", "opportunity zone", "tax incentive"]),
    ("Manufactured & Modular", ["manufactured hous", "manufactured home", "modular",
                                "factory-built"]),
    ("Rural Housing", ["rural housing", "rural development", "usda"]),
    ("Veterans Housing", ["veteran"]),
    ("Environmental Review", ["environmental review", "nepa"]),
    ("Homeownership", ["homeowner", "homeownership", "first-time homebuyer", "down payment"]),
    ("Disaster Recovery", ["disaster", "resilience"]),
    ("Community Development", ["community development", "cdbg", "block grant"]),
]

# Core housing-policy mechanisms the Center studies. A bill carrying any of
# these tags is flagged as a potential bill to watch.
WATCH_TAGS = {
    "Zoning & Land Use", "Housing Supply", "Affordable Housing", "Rent Regulation",
    "Tenant Protections", "Public Housing", "Vouchers & Rental Assistance", "Homelessness",
}

MAX_TAGS = 4


def derive_tags(text: str) -> list[str]:
    """A few topic tags for a bill, scored by keyword hits in title+summary."""
    t = (text or "").lower()
    scored: list[tuple[int, str]] = []
    for tag, keywords in TAG_RULES:
        hits = 0
        for kw in keywords:
            if len(kw) <= 5:
                hits += len(re.findall(rf"\b{re.escape(kw)}\b", t))
            else:
                hits += t.count(kw)
        if hits:
            scored.append((hits, tag))
    scored.sort(key=lambda x: (-x[0], x[1]))
    return [tag for _, tag in scored[:MAX_TAGS]]


def is_watch(tags: list[str] | None) -> bool:
    return bool(set(tags or []) & WATCH_TAGS)


async def store_tracker_bill(
    con: AsyncConnection, parsed: dict, bill_json: dict, summaries_json: dict | None,
    *, tracking: str = "tracked",
) -> str:
    """Upsert a tracker bill + sponsors + summaries + raw payload.

    `tracking` applies only on first insert; a re-sync never overwrites the
    curated tracked/untracked choice.
    """
    slug = bill_slug(parsed["congress"], parsed["bill_type"], parsed["bill_number"])
    vintage = datetime.now(timezone.utc)

    async with con.transaction():
        await con.execute(
            """
            INSERT INTO bills (bill_id, congress, bill_type, bill_number, title,
                               origin_chamber, latest_action_date, latest_action_text,
                               update_date, source_id, data_vintage,
                               introduced_date, policy_area, tracking_status)
            VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,'congress_gov',%s,%s,%s,%s)
            ON CONFLICT (bill_id) DO UPDATE SET
                title = EXCLUDED.title,
                origin_chamber = EXCLUDED.origin_chamber,
                latest_action_date = EXCLUDED.latest_action_date,
                latest_action_text = EXCLUDED.latest_action_text,
                update_date = EXCLUDED.update_date,
                data_vintage = EXCLUDED.data_vintage,
                introduced_date = EXCLUDED.introduced_date,
                policy_area = EXCLUDED.policy_area
            """,
            (slug, parsed["congress"], parsed["bill_type"], parsed["bill_number"],
             parsed["title"], parsed["origin_chamber"], parsed["latest_action_date"],
             parsed["latest_action_text"], parsed["update_date"], vintage,
             parsed["introduced_date"], parsed["policy_area"], tracking),
        )

        await con.execute("DELETE FROM bill_sponsors WHERE bill_id = %s", (slug,))
        for s in parsed["sponsors"]:
            await con.execute(
                """
                INSERT INTO bill_sponsors (bill_id, bioguide_id, full_name, first_name,
                                           last_name, party, state, district,
                                           is_by_request, url)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)
                """,
                (slug, s["bioguide_id"], s["full_name"], s["first_name"], s["last_name"],
                 s["party"], s["state"], s["district"], str(s["is_by_request"] or ""),
                 s["url"]),
            )

        summary_texts: list[str] = []
        if summaries_json is not None:
            await con.execute("DELETE FROM bill_summaries WHERE bill_id = %s", (slug,))
            for sm in summaries_json.get("summaries", []) or []:
                text = strip_html(sm.get("text"))
                summary_texts.append(text)
                await con.execute(
                    """
                    INSERT INTO bill_summaries (bill_id, version_code, action_date,
                                                action_desc, update_date, text)
                    VALUES (%s,%s,%s,%s,%s,%s)
                    """,
                    (slug, sm.get("versionCode"), sm.get("actionDate"),
                     sm.get("actionDesc"), sm.get("updateDate"), text),
                )

        # Tagging. The CRS-summary scan is authoritative and runs once — when
        # the summary first arrives (it upgrades provisional title-scan tags,
        # but never touches 'summary' or 'manual' tags). Bills with no summary
        # yet get provisional tags from title + latest action so the review
        # list is still usable before CRS publishes.
        async with con.cursor() as cur:
            await cur.execute(
                "SELECT tags, tags_source FROM bills WHERE bill_id = %s", (slug,)
            )
            existing_tags, tags_source = (await cur.fetchone()) or ([], None)
        if summary_texts and tags_source in (None, "title"):
            tags = derive_tags((parsed["title"] or "") + " " + " ".join(summary_texts))
            await con.execute(
                "UPDATE bills SET tags = %s, tags_source = 'summary' WHERE bill_id = %s",
                (tags, slug),
            )
        elif not summary_texts and not existing_tags and tags_source is None:
            tags = derive_tags(
                (parsed["title"] or "") + " " + (parsed["latest_action_text"] or "")
            )
            if tags:
                await con.execute(
                    "UPDATE bills SET tags = %s, tags_source = 'title' WHERE bill_id = %s",
                    (tags, slug),
                )

        await con.execute(
            """
            INSERT INTO raw_payloads (bill_id, endpoint, fetched_at, http_status, payload_json)
            VALUES (%s,'bill',%s,200,%s)
            """,
            (slug, vintage, Json(bill_json)),
        )
    return slug


async def _stored_update_dates(con: AsyncConnection, congress: int) -> dict[str, str]:
    async with con.cursor() as cur:
        await cur.execute(
            "SELECT bill_id, update_date::text FROM bills WHERE congress = %s", (congress,)
        )
        return {r[0]: r[1] for r in await cur.fetchall()}


async def _ingest_detail(
    con: AsyncConnection, client: CongressClient,
    congress: int, bill_type: str, bill_number: int,
    *, tracking: str = "tracked", require_housing: bool = True, refresh: bool = True,
) -> str | None:
    """Detail-fetch one bill; store it (housing bills only by default)."""
    bill_json = await client.fetch_bill(congress, bill_type, bill_number, refresh=refresh)
    parsed = parse_tracker_bill(bill_json)
    if require_housing and parsed["policy_area"] != config.HOUSING_POLICY_AREA:
        return None
    try:
        summaries = await client.fetch_bill_summaries(congress, bill_type, bill_number)
    except BillNotFound:
        summaries = None
    return await store_tracker_bill(con, parsed, bill_json, summaries, tracking=tracking)


async def get_sync_state(con: AsyncConnection, key: str) -> str | None:
    async with con.cursor() as cur:
        await cur.execute("SELECT value FROM sync_state WHERE key = %s", (key,))
        row = await cur.fetchone()
        return row[0] if row else None


async def set_sync_state(con: AsyncConnection, key: str, value: str) -> None:
    await con.execute(
        """
        INSERT INTO sync_state (key, value, updated_at) VALUES (%s, %s, now())
        ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now()
        """,
        (key, value),
    )


async def sync_seed(
    con: AsyncConnection, client: CongressClient,
    congress: int, bill_type: str, bill_number: int,
) -> dict:
    """Ingest one bill and the housing bills among its /relatedbills."""
    s = config.get_settings()
    stored: list[str] = []
    checked = 0

    slug = await _ingest_detail(con, client, congress, bill_type, bill_number)
    checked += 1
    if slug:
        stored.append(slug)

    related = await client.fetch_related_bills(congress, bill_type, bill_number)
    for rb in related.get("relatedBills", []) or []:
        if checked >= s.sync_max_detail_calls:
            break
        if rb.get("congress") != congress:
            continue
        checked += 1
        try:
            rslug = await _ingest_detail(
                con, client, rb["congress"], rb["type"].lower(), int(rb["number"])
            )
        except BillNotFound:
            continue
        if rslug:
            stored.append(rslug)
    return {"mode": "seed", "checked": checked, "stored": stored}


async def sync_window(
    con: AsyncConnection, client: CongressClient, congress: int, days: int
) -> dict:
    """Walk recently-updated bills and ingest the housing ones.

    Skips detail fetches for bills whose stored update_date already matches the
    list row, so a routine daily run only pays for genuinely changed bills.
    """
    s = config.get_settings()
    from_dt = (datetime.now(timezone.utc) - timedelta(days=days)).strftime("%Y-%m-%dT%H:%M:%SZ")
    known = await _stored_update_dates(con, congress)

    checked_pages = 0
    detail_calls = 0
    listed = 0
    stored: list[str] = []

    offset = 0
    while checked_pages < s.sync_max_list_pages and detail_calls < s.sync_max_detail_calls:
        page = await client.fetch_bill_list(congress, offset=offset, limit=250, from_dt=from_dt)
        rows = page.get("bills", []) or []
        checked_pages += 1
        listed += len(rows)
        if not rows:
            break
        for row in rows:
            if detail_calls >= s.sync_max_detail_calls:
                break
            slug = bill_slug(row["congress"], row["type"].lower(), int(row["number"]))
            upstream_update = row.get("updateDate") or ""
            known_update = known.get(slug)
            if known_update is not None and known_update.startswith(upstream_update[:19]):
                continue  # our copy is current
            detail_calls += 1
            try:
                rslug = await _ingest_detail(
                    con, client, row["congress"], row["type"].lower(), int(row["number"])
                )
            except BillNotFound:
                continue
            if rslug:
                stored.append(rslug)
        if page.get("pagination", {}).get("next") is None:
            break
        offset += 250

    return {
        "mode": "window", "days": days, "listed": listed,
        "detail_calls": detail_calls, "stored": stored,
    }


async def _store_texts_for(
    con: AsyncConnection, client: CongressClient,
    bill_id: str, congress: int, bill_type: str, bill_number: int,
) -> int:
    """Fetch + store all Formatted-Text bodies for one bill. Returns HTTP calls made."""
    from .repository import parse_text_versions

    calls = 1
    try:
        text_json = await client.fetch_bill_text(congress, bill_type, bill_number, refresh=True)
    except BillNotFound:
        return calls
    versions = [v for v in parse_text_versions(text_json)
                if v["format_type"] == "Formatted Text" and v["url"]]
    if not versions:
        return calls
    bodies: dict[str, str] = {}
    for v in versions:
        calls += 1
        bodies[v["url"]] = await client.fetch_text_body(v["url"])

    vintage = datetime.now(timezone.utc)
    async with con.transaction():
        await con.execute("DELETE FROM bill_text_versions WHERE bill_id = %s", (bill_id,))
        for v in versions:
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
                (bill_id, v["version_code"], v["version_name"], v["version_date"],
                 v["format_type"], v["url"], bodies.get(v["url"])),
            )
        await con.execute(
            "UPDATE bills SET data_vintage = %s WHERE bill_id = %s", (vintage, bill_id)
        )
    return calls


async def refresh_tracked(con: AsyncConnection, client: CongressClient, congress: int) -> dict:
    """Re-sync every tracked bill: status/sponsors/summaries, plus full text
    for tracked bills whose text is missing or whose upstream copy changed."""
    s = config.get_settings()
    async with con.cursor() as cur:
        await cur.execute(
            """
            SELECT b.bill_id, b.congress, b.bill_type, b.bill_number, b.update_date::text,
                   EXISTS (SELECT 1 FROM bill_text_versions tv
                           WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL)
            FROM bills b
            WHERE b.tracking_status = 'tracked' AND b.congress = %s
            ORDER BY b.latest_action_date DESC NULLS LAST
            """,
            (congress,),
        )
        targets = await cur.fetchall()

    calls = 0
    refreshed: list[str] = []
    texts_pulled: list[str] = []
    for bill_id, b_congress, bill_type, bill_number, old_update, had_text in targets:
        if calls >= s.sync_max_detail_calls:
            break
        calls += 2  # detail + summaries
        try:
            slug = await _ingest_detail(con, client, b_congress, bill_type, bill_number,
                                        require_housing=False)
        except BillNotFound:
            continue
        refreshed.append(slug or bill_id)
        new_update = await get_bill_update_date(con, bill_id)
        changed = old_update is None or new_update is None or not old_update.startswith(new_update[:19])
        if not had_text or changed:
            calls += await _store_texts_for(con, client, bill_id, b_congress, bill_type, bill_number)
            texts_pulled.append(bill_id)

    await set_sync_state(con, "last_refresh_at", datetime.now(timezone.utc).isoformat())
    return {"mode": "refresh", "bills": len(targets), "refreshed": len(refreshed),
            "texts_pulled": texts_pulled, "calls": calls}


async def get_bill_update_date(con: AsyncConnection, bill_id: str) -> str | None:
    async with con.cursor() as cur:
        await cur.execute("SELECT update_date::text FROM bills WHERE bill_id = %s", (bill_id,))
        row = await cur.fetchone()
        return row[0] if row else None


async def discover_new(
    con: AsyncConnection, client: CongressClient, congress: int, days: int
) -> dict:
    """Find housing bills updated in the window that are not yet in our table.

    Nothing is stored — candidates are returned for the admin to accept as
    tracked (with full text) or untracked. Bounded by the config sync caps.
    """
    s = config.get_settings()
    from_dt = (datetime.now(timezone.utc) - timedelta(days=days)).strftime("%Y-%m-%dT%H:%M:%SZ")
    async with con.cursor() as cur:
        await cur.execute("SELECT bill_id FROM bills WHERE congress = %s", (congress,))
        known = {r[0] for r in await cur.fetchall()}

    candidates: list[dict] = []
    pages = 0
    detail_calls = 0
    listed = 0
    offset = 0
    while pages < s.sync_max_list_pages and detail_calls < s.sync_max_detail_calls:
        page = await client.fetch_bill_list(congress, offset=offset, limit=250, from_dt=from_dt)
        rows = page.get("bills", []) or []
        pages += 1
        listed += len(rows)
        if not rows:
            break
        for row in rows:
            if detail_calls >= s.sync_max_detail_calls:
                break
            slug = bill_slug(row["congress"], row["type"].lower(), int(row["number"]))
            if slug in known:
                continue
            detail_calls += 1
            try:
                bill_json = await client.fetch_bill(
                    row["congress"], row["type"].lower(), int(row["number"]), refresh=True
                )
            except BillNotFound:
                continue
            parsed = parse_tracker_bill(bill_json)
            if parsed["policy_area"] != config.HOUSING_POLICY_AREA:
                continue
            sponsor = parsed["sponsors"][0]["full_name"] if parsed["sponsors"] else None
            # Provisional tags from title + latest action; the real scan runs
            # against the CRS summary when the bill is added.
            cand_tags = derive_tags(
                (parsed["title"] or "") + " " + (parsed["latest_action_text"] or "")
            )
            candidates.append({
                "congress": parsed["congress"],
                "bill_type": parsed["bill_type"],
                "bill_number": parsed["bill_number"],
                "ref": format_ref(parsed["bill_type"], parsed["bill_number"]),
                "title": parsed["title"],
                "chamber": parsed["origin_chamber"],
                "sponsor": sponsor,
                "introduced": parsed["introduced_date"],
                "latest_action_date": parsed["latest_action_date"],
                "latest_action_text": parsed["latest_action_text"],
                "status_key": status_key(parsed["latest_action_text"]),
                "tags": cand_tags,
                "watch": is_watch(cand_tags),
            })
        if page.get("pagination", {}).get("next") is None:
            break
        offset += 250

    await set_sync_state(con, "last_discovery_at", datetime.now(timezone.utc).isoformat())
    return {"mode": "discover", "days": days, "listed": listed,
            "detail_calls": detail_calls, "candidates": candidates}


async def add_bill(
    con: AsyncConnection, client: CongressClient,
    congress: int, bill_type: str, bill_number: int, *, tracked: bool,
) -> str | None:
    """Ingest one bill as tracked (with full text) or untracked (metadata only)."""
    tracking = "tracked" if tracked else "untracked"
    # Discovery already cached the detail JSON on disk, so refresh=False is cheap.
    slug = await _ingest_detail(con, client, congress, bill_type, bill_number,
                                tracking=tracking, require_housing=False, refresh=False)
    if slug and tracked:
        await _store_texts_for(con, client, slug, congress, bill_type, bill_number)
    return slug


async def refresh_one(
    con: AsyncConnection, client: CongressClient, bill_id: str
) -> dict | None:
    """Re-pull one bill's latest version: detail, summaries, and (when the
    bill is tracked) its full text."""
    async with con.cursor() as cur:
        await cur.execute(
            "SELECT congress, bill_type, bill_number, tracking_status "
            "FROM bills WHERE bill_id = %s",
            (bill_id,),
        )
        row = await cur.fetchone()
    if row is None:
        return None
    congress, bill_type, bill_number, tracking_status = row
    slug = await _ingest_detail(con, client, congress, bill_type, bill_number,
                                require_housing=False)
    texts_pulled = False
    if tracking_status == "tracked":
        await _store_texts_for(con, client, bill_id, congress, bill_type, bill_number)
        texts_pulled = True
    async with con.cursor() as cur:
        await cur.execute(
            "SELECT EXISTS (SELECT 1 FROM bill_summaries WHERE bill_id = %s)", (bill_id,)
        )
        has_summary = (await cur.fetchone())[0]
    return {"bill_id": slug or bill_id, "texts_pulled": texts_pulled,
            "has_summary": has_summary}


async def set_tracking(
    con: AsyncConnection, client: CongressClient, bill_id: str, *, tracked: bool
) -> dict | None:
    """Flip a bill between tracked and untracked; pull full text when tracking."""
    async with con.cursor() as cur:
        await cur.execute(
            "SELECT congress, bill_type, bill_number FROM bills WHERE bill_id = %s",
            (bill_id,),
        )
        row = await cur.fetchone()
    if row is None:
        return None
    tracking = "tracked" if tracked else "untracked"
    await con.execute(
        "UPDATE bills SET tracking_status = %s WHERE bill_id = %s", (tracking, bill_id)
    )
    texts_pulled = False
    if tracked:
        async with con.cursor() as cur:
            await cur.execute(
                "SELECT EXISTS (SELECT 1 FROM bill_text_versions "
                "WHERE bill_id = %s AND text_content IS NOT NULL)", (bill_id,),
            )
            has_text = (await cur.fetchone())[0]
        if not has_text:
            await _store_texts_for(con, client, bill_id, row[0], row[1], row[2])
            texts_pulled = True
    return {"bill_id": bill_id, "tracking_status": tracking, "texts_pulled": texts_pulled}


async def sync_texts(
    con: AsyncConnection, client: CongressClient, congress: int,
    *, refresh: bool = False,
) -> dict:
    """Pull full bill text (Formatted Text only — never PDF) for tracker bills.

    On-command: for each housing bill in `bills`, fetch /text, download the
    Formatted Text body of each version, and store it in bill_text_versions.
    Bills that already have stored text are skipped unless refresh=True.
    Each stored bill's data_vintage (our retrieval stamp) is updated;
    update_date stays congress.gov's own timestamp so window-sync staleness
    checks keep working.
    """
    from .repository import parse_text_versions  # local import avoids cycle

    s = config.get_settings()
    async with con.cursor() as cur:
        await cur.execute(
            """
            SELECT b.bill_id, b.congress, b.bill_type, b.bill_number
            FROM bills b
            WHERE b.policy_area = %s AND b.congress = %s
              AND (%s OR NOT EXISTS (
                    SELECT 1 FROM bill_text_versions tv
                    WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL))
            ORDER BY b.latest_action_date DESC NULLS LAST
            """,
            (config.HOUSING_POLICY_AREA, congress, refresh),
        )
        targets = await cur.fetchall()

    fetched: list[str] = []
    calls = 0
    for bill_id, b_congress, bill_type, bill_number in targets:
        if calls >= s.sync_max_detail_calls:
            break
        calls += 1
        try:
            text_json = await client.fetch_bill_text(b_congress, bill_type, bill_number,
                                                     refresh=True)
        except BillNotFound:
            continue
        versions = [v for v in parse_text_versions(text_json)
                    if v["format_type"] == "Formatted Text" and v["url"]]
        if not versions:
            continue
        bodies: dict[str, str] = {}
        for v in versions:
            calls += 1
            bodies[v["url"]] = await client.fetch_text_body(v["url"])

        vintage = datetime.now(timezone.utc)
        async with con.transaction():
            await con.execute("DELETE FROM bill_text_versions WHERE bill_id = %s", (bill_id,))
            for v in versions:
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
                    (bill_id, v["version_code"], v["version_name"], v["version_date"],
                     v["format_type"], v["url"], bodies.get(v["url"])),
                )
            await con.execute(
                "UPDATE bills SET data_vintage = %s WHERE bill_id = %s",
                (vintage, bill_id),
            )
        fetched.append(bill_id)

    return {"mode": "texts", "candidates": len(targets), "fetched": fetched, "calls": calls}


async def set_display(con: AsyncConnection, bill_id: str, displayed: bool) -> dict | None:
    """Publish (display_date = now) or unpublish (NULL) a bill."""
    async with con.cursor() as cur:
        await cur.execute(
            "UPDATE bills SET display_date = CASE WHEN %s THEN now() ELSE NULL END "
            "WHERE bill_id = %s RETURNING display_date",
            (displayed, bill_id),
        )
        row = await cur.fetchone()
    if row is None:
        return None
    return {"bill_id": bill_id,
            "display_date": row[0].isoformat() if row[0] else None}


async def set_pinned(con: AsyncConnection, bill_id: str, pinned: bool) -> dict | None:
    async with con.cursor() as cur:
        await cur.execute(
            "UPDATE bills SET pinned = %s WHERE bill_id = %s RETURNING pinned",
            (pinned, bill_id),
        )
        row = await cur.fetchone()
    if row is None:
        return None
    return {"bill_id": bill_id, "pinned": row[0]}


async def list_tracker(
    con: AsyncConnection, *, policy_area: str | None, congress: int | None, limit: int,
    view: str = "public", tracking: str = "all", q: str | None = None,
) -> list[dict]:
    """Rows for GET /legislation, shaped for the front-end tracker tiles.

    view: 'public' — only bills whose display_date has arrived, pinned first.
          'admin'  — every bill, with the tracking filter applied.
    tracking (admin view): 'tracked', 'untracked', or 'all'.
    q: case-insensitive substring match over ref/title/sponsor/summary.
    """
    sql = """
        SELECT b.bill_id, b.congress, b.bill_type, b.bill_number, b.title,
               b.origin_chamber, b.latest_action_date, b.latest_action_text,
               b.introduced_date, b.policy_area, b.tracking_status, b.tags, b.tags_source,
               b.display_date, b.pinned,
               sp.full_name AS sponsor_name, sp.party AS sponsor_party,
               sp.state AS sponsor_state,
               sm.text AS summary,
               EXISTS (SELECT 1 FROM bill_text_versions tv
                       WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL)
                   AS has_text
        FROM bills b
        LEFT JOIN LATERAL (
            SELECT full_name, party, state FROM bill_sponsors
            WHERE bill_id = b.bill_id LIMIT 1
        ) sp ON TRUE
        LEFT JOIN LATERAL (
            SELECT text FROM bill_summaries
            WHERE bill_id = b.bill_id
            ORDER BY action_date DESC NULLS LAST LIMIT 1
        ) sm ON TRUE
        WHERE TRUE
    """
    params: list = []
    if policy_area:
        sql += " AND b.policy_area = %s"
        params.append(policy_area)
    if view == "public":
        sql += " AND b.display_date IS NOT NULL AND b.display_date <= now()"
    elif tracking in ("tracked", "untracked"):
        sql += " AND b.tracking_status = %s"
        params.append(tracking)
    if congress is not None:
        sql += " AND b.congress = %s"
        params.append(congress)
    if q:
        sql += """ AND (b.title ILIKE %s OR b.bill_id ILIKE %s
                        OR sp.full_name ILIKE %s OR sm.text ILIKE %s
                        OR b.latest_action_text ILIKE %s)"""
        like = f"%{q}%"
        params += [like, like, like, like, like]
    if view == "public":
        sql += " ORDER BY b.pinned DESC, b.latest_action_date DESC NULLS LAST LIMIT %s"
    else:
        sql += " ORDER BY b.latest_action_date DESC NULLS LAST LIMIT %s"
    params.append(limit)

    async with con.cursor(row_factory=dict_row) as cur:
        await cur.execute(sql, params)
        rows = await cur.fetchall()

    out = []
    for r in rows:
        out.append({
            "bill_id": r["bill_id"],
            "tracking_status": r["tracking_status"],
            "has_text": r["has_text"],
            "tags": r["tags"] or [],
            "tags_source": r["tags_source"],
            "watch": is_watch(r["tags"]),
            "display_date": r["display_date"].isoformat() if r["display_date"] else None,
            "displayed": bool(r["display_date"] and r["display_date"] <= datetime.now(timezone.utc)),
            "pinned": r["pinned"],
            "ref": format_ref(r["bill_type"], r["bill_number"]),
            "congress": congress_ordinal(r["congress"]),
            "chamber": r["origin_chamber"],
            "title": r["title"],
            "status_key": status_key(r["latest_action_text"]),
            "status_text": r["latest_action_text"],
            "updated": r["latest_action_date"].isoformat() if r["latest_action_date"] else None,
            "introduced": r["introduced_date"].isoformat() if r["introduced_date"] else None,
            "category": r["policy_area"],
            "sponsor": r["sponsor_name"],
            "sponsor_party": r["sponsor_party"],
            "sponsor_state": r["sponsor_state"],
            "summary": r["summary"],
            "congress_gov_url": (
                f"https://www.congress.gov/bill/{congress_ordinal(r['congress'])}-congress/"
                + {"hr": "house-bill", "s": "senate-bill", "hres": "house-resolution",
                   "sres": "senate-resolution", "hjres": "house-joint-resolution",
                   "sjres": "senate-joint-resolution", "hconres": "house-concurrent-resolution",
                   "sconres": "senate-concurrent-resolution"}.get(r["bill_type"], r["bill_type"])
                + f"/{r['bill_number']}"
            ),
        })
    return out
