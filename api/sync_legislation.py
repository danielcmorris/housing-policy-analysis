"""
CLI: maintain the housing-legislation tracker table.

Usage (from the repo root, venv active):

    python -m api.sync_legislation --window-days 7
    python -m api.sync_legislation --seed 119/hr/6644
    python -m api.sync_legislation --seed 119/hr/6644 --window-days 30

Runs are bounded by the hard caps in api/app/config.py
(sync_max_list_pages, sync_max_detail_calls). Run daily via cron for a
continuously current tracker.
"""
from __future__ import annotations

import argparse
import asyncio

from .app import config
from .app.congress_client import CongressClient
from .app.db import init_schema, make_pool
from .app.legislation import sync_seed, sync_texts, sync_window


def parse_seed(value: str) -> tuple[int, str, int]:
    try:
        congress, bill_type, number = value.split("/")
        return int(congress), bill_type.lower(), int(number)
    except ValueError:
        raise argparse.ArgumentTypeError("seed must look like 119/hr/6644")


async def run(args: argparse.Namespace) -> None:
    pool = make_pool()
    await pool.open()
    await init_schema(pool)
    settings = config.get_settings()
    try:
        async with CongressClient(api_key=settings.congress_api_key) as client:
            async with pool.connection() as con:
                for congress, bill_type, number in args.seed:
                    result = await sync_seed(con, client, congress, bill_type, number)
                    print(f"seed {congress}/{bill_type}/{number}: "
                          f"checked={result['checked']} stored={len(result['stored'])}")
                    for slug in result["stored"]:
                        print(f"  + {slug}")
                if args.window_days:
                    result = await sync_window(con, client, args.congress, args.window_days)
                    print(f"window {args.window_days}d: listed={result['listed']} "
                          f"detail_calls={result['detail_calls']} stored={len(result['stored'])}")
                    for slug in result["stored"]:
                        print(f"  + {slug}")
                if args.texts:
                    result = await sync_texts(con, client, args.congress,
                                              refresh=args.refresh_texts)
                    print(f"texts: candidates={result['candidates']} "
                          f"fetched={len(result['fetched'])} calls={result['calls']}")
                    for slug in result["fetched"]:
                        print(f"  + {slug}")
    finally:
        await pool.close()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--congress", type=int, default=119)
    ap.add_argument("--seed", type=parse_seed, action="append", default=[],
                    help="congress/type/number, e.g. 119/hr/6644 (repeatable)")
    ap.add_argument("--window-days", type=int, default=0,
                    help="also sync bills updated in the last N days")
    ap.add_argument("--texts", action="store_true",
                    help="pull full Formatted-Text bodies for tracker bills missing them")
    ap.add_argument("--refresh-texts", action="store_true",
                    help="with --texts: re-pull text even for bills that already have it")
    args = ap.parse_args()
    if not args.seed and not args.window_days and not args.texts:
        ap.error("nothing to do: pass --seed, --window-days, and/or --texts")
    asyncio.run(run(args))


if __name__ == "__main__":
    main()
