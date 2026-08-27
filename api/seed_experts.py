"""
CLI: seed the experts table from the public roster JSON.

    python -m api.seed_experts                       # ui/web-ui/public/data/experts.json
    python -m api.seed_experts path/to/experts.json

Upserts by slug (derived from the name), so re-running is safe and never
clobbers fields the roster file doesn't carry (bio, LinkedIn, conflicts, ...)
— only the roster-sourced columns are refreshed.
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import sys

from .app import config
from .app.db import init_schema, make_pool

DEFAULT_ROSTER = os.path.join(
    config.REPO_ROOT, "ui", "web-ui", "public", "data", "experts.json"
)


def slugify(name: str) -> str:
    return re.sub(r"^-+|-+$", "", re.sub(r"[^a-z0-9]+", "-", name.lower()))


async def run(path: str) -> None:
    with open(path, encoding="utf-8") as f:
        roster = json.load(f).get("experts") or []
    if not roster:
        raise SystemExit(f"no experts found in {path}")

    pool = make_pool()
    await pool.open()
    await init_schema(pool)
    try:
        async with pool.connection() as con:
            for e in roster:
                await con.execute(
                    """
                    INSERT INTO experts (slug, full_name, title, affiliation, category,
                                         focus, profile_url, image_url, joined_at)
                    VALUES (%(slug)s, %(name)s, %(title)s, %(affiliation)s, %(category)s,
                            %(focus)s, %(profile_url)s, %(image_url)s, CURRENT_DATE)
                    ON CONFLICT (slug) DO UPDATE SET
                        full_name = EXCLUDED.full_name,
                        title = EXCLUDED.title,
                        affiliation = EXCLUDED.affiliation,
                        category = EXCLUDED.category,
                        focus = EXCLUDED.focus,
                        profile_url = EXCLUDED.profile_url,
                        image_url = EXCLUDED.image_url,
                        updated_at = now()
                    """,
                    {
                        "slug": slugify(e["name"]),
                        "name": e["name"],
                        "title": e.get("title"),
                        "affiliation": e.get("affiliation"),
                        "category": e.get("category"),
                        "focus": e.get("focus"),
                        "profile_url": e.get("profile_url"),
                        "image_url": e.get("image_url"),
                    },
                )
    finally:
        await pool.close()
    print(f"seeded {len(roster)} expert(s) from {path}")


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_ROSTER
    asyncio.run(run(path))


if __name__ == "__main__":
    main()
