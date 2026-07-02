"""
Postgres access: an async psycopg3 connection pool and schema bootstrap.

schema.sql is the source of truth (same idiom as the proof slice, which runs
its schema.sql via executescript on startup). init_schema() applies it
idempotently — every statement is CREATE ... IF NOT EXISTS — so calling it on
every boot is safe.
"""
from __future__ import annotations

import os

from psycopg_pool import AsyncConnectionPool

from . import config

SCHEMA_PATH = os.path.join(config.REPO_ROOT, "api", "schema.sql")


def make_pool(conninfo: str | None = None) -> AsyncConnectionPool:
    """Create (but do not open) an async pool. Caller opens it in the app lifespan."""
    dsn = conninfo or config.get_settings().database_url
    return AsyncConnectionPool(dsn, min_size=1, max_size=10, open=False)


async def init_schema(pool: AsyncConnectionPool) -> None:
    """Apply schema.sql and ensure the congress_gov source row exists."""
    with open(SCHEMA_PATH, encoding="utf-8") as f:
        ddl = f.read()
    async with pool.connection() as con:
        await con.execute(ddl)
        s = config.SOURCE
        await con.execute(
            """
            INSERT INTO sources (source_id, name, publisher, url)
            VALUES (%s, %s, %s, %s)
            ON CONFLICT (source_id) DO UPDATE
              SET name = EXCLUDED.name,
                  publisher = EXCLUDED.publisher,
                  url = EXCLUDED.url
            """,
            (s["source_id"], s["name"], s["publisher"], s["url"]),
        )
