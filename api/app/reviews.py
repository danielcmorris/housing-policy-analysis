"""
Bill reviews: the Center's authored four-stage analyses, stored as JSONB
(shape: prototype/data/bill-review.schema.json) and served with live
legislative status merged in from the `bills` table.

The editorial document (provisions, precedents, projections, peer reviews,
outlook) is authored content and is returned verbatim. Only status is
live-merged: when the congressional record says the bill has reached a
terminal-ish stage our coarse classifier is sure about (enacted, to
president, failed), meta.status is overridden and the legislative timeline
is completed accordingly — so the review page never contradicts the tracker.
"""
from __future__ import annotations

import json
import re
from datetime import datetime, timezone

from psycopg import AsyncConnection
from psycopg.rows import dict_row
from psycopg.types.json import Json

from .legislation import status_key

REVIEW_ID_RE = re.compile(r"^([a-z]+)(\d+)-(\d+)$")  # e.g. hr6644-119


def review_id_to_slug(review_id: str) -> str | None:
    """'hr6644-119' -> '119-hr-6644' (the bills.bill_id key)."""
    m = REVIEW_ID_RE.match(review_id)
    if not m:
        return None
    bill_type, number, congress = m.groups()
    return f"{congress}-{bill_type}-{number}"


def merge_live_status(doc: dict, latest_action_text: str | None,
                      latest_action_date) -> dict:
    """Overlay live status from the congressional record onto a review doc."""
    sk = status_key(latest_action_text)
    if sk not in ("enacted", "to_president", "failed"):
        return doc  # editorial status (e.g. awaiting concurrence) is richer

    doc = json.loads(json.dumps(doc))  # deep copy; never mutate the stored row
    meta = doc.setdefault("meta", {})
    meta["status"] = sk
    date_str = latest_action_date.isoformat() if latest_action_date else None

    stages = meta.get("legislativeStatus") or []
    if sk == "enacted":
        for s in stages:
            if s.get("state") == "in_progress":
                s["state"] = "complete"
        if not any(s.get("stage") == "enacted" for s in stages):
            stages.append({"stage": "enacted", "state": "complete", "date": date_str})
        meta["legislativeStatus"] = stages
    elif sk == "to_president":
        if not any(s.get("stage") == "to_president" for s in stages):
            stages.append({"stage": "to_president", "state": "in_progress", "date": date_str})
        meta["legislativeStatus"] = stages
    return doc


async def get_review_store(con: AsyncConnection) -> dict:
    """All reviews as the front-end store shape: {featuredBillId, bills{}}.

    The featured review is the one whose bill is pinned on the public
    tracker (falling back to the most recently updated review).
    """
    async with con.cursor(row_factory=dict_row) as cur:
        await cur.execute(
            """
            SELECT r.review_id, r.review, b.latest_action_text, b.latest_action_date,
                   COALESCE(b.pinned, FALSE) AS pinned
            FROM bill_reviews r
            LEFT JOIN bills b ON b.bill_id = r.bill_id
            ORDER BY COALESCE(b.pinned, FALSE) DESC, r.updated_at DESC
            """
        )
        rows = await cur.fetchall()

    bills = {
        r["review_id"]: merge_live_status(
            r["review"], r["latest_action_text"], r["latest_action_date"]
        )
        for r in rows
    }
    return {
        "version": "1.0",
        "defaultLocale": "en",
        "featuredBillId": rows[0]["review_id"] if rows else None,
        "bills": bills,
    }


async def get_review(con: AsyncConnection, review_id: str) -> dict | None:
    async with con.cursor(row_factory=dict_row) as cur:
        await cur.execute(
            """
            SELECT r.review, b.latest_action_text, b.latest_action_date
            FROM bill_reviews r
            LEFT JOIN bills b ON b.bill_id = r.bill_id
            WHERE r.review_id = %s
            """,
            (review_id,),
        )
        row = await cur.fetchone()
    if row is None:
        return None
    return merge_live_status(row["review"], row["latest_action_text"],
                             row["latest_action_date"])


async def upsert_review(con: AsyncConnection, review_id: str, doc: dict) -> str:
    """Store or replace a review document, linking it to its bills row."""
    slug = review_id_to_slug(review_id)
    if slug is not None:
        async with con.cursor() as cur:
            await cur.execute("SELECT 1 FROM bills WHERE bill_id = %s", (slug,))
            if await cur.fetchone() is None:
                slug = None  # keep the review even if the bill row is absent
    await con.execute(
        """
        INSERT INTO bill_reviews (review_id, bill_id, review, updated_at)
        VALUES (%s, %s, %s, %s)
        ON CONFLICT (review_id) DO UPDATE SET
            bill_id = EXCLUDED.bill_id,
            review = EXCLUDED.review,
            updated_at = EXCLUDED.updated_at
        """,
        (review_id, slug, Json(doc), datetime.now(timezone.utc)),
    )
    return review_id
