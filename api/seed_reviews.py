"""
CLI: load bill-review documents into the bill_reviews table.

    python -m api.seed_reviews                      # seed from the app's bills.json
    python -m api.seed_reviews path/to/reviews.json # seed from another store file

The input is the front-end store shape ({"featuredBillId": ..., "bills":
{review_id: document}}) — the same file the Angular app previously served
statically. Upserts are idempotent.
"""
from __future__ import annotations

import asyncio
import json
import os
import sys

from .app import config
from .app.db import init_schema, make_pool
from .app.reviews import upsert_review

DEFAULT_STORE = os.path.join(
    config.REPO_ROOT, "ui", "web-ui", "public", "data", "bills.json"
)


async def run(path: str) -> None:
    with open(path, encoding="utf-8") as f:
        store = json.load(f)
    bills = store.get("bills") or {}
    if not bills:
        raise SystemExit(f"no reviews found in {path}")

    pool = make_pool()
    await pool.open()
    await init_schema(pool)
    try:
        async with pool.connection() as con:
            for review_id, doc in bills.items():
                await upsert_review(con, review_id, doc)
                print(f"  + {review_id}")
    finally:
        await pool.close()
    print(f"seeded {len(bills)} review(s) from {path}")


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_STORE
    asyncio.run(run(path))


if __name__ == "__main__":
    main()
