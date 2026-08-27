"""
CLI: embed pending document chunks with the local Ollama embedding model.

    python -m api.embed_chunks                  # embed everything pending
    python -m api.embed_chunks --limit 200      # bounded run
    python -m api.embed_chunks --re-embed       # wipe + redo (model change)

Documents AND search queries must use the same model (vectors from different
models are incomparable); the model here must match Ollama:EmbedModel in
HousingPolicy.Api's configuration. Runs against Ollama on the LAN server —
no cloud calls, no API cost.
"""
from __future__ import annotations

import argparse
import asyncio
import json
import urllib.request

from .app.db import init_schema, make_pool

OLLAMA_URL = "http://192.168.168.200:11434/api/embed"
MODEL = "nomic-embed-text"
BATCH = 32
DIMS = 768


def embed_batch(texts: list[str]) -> list[list[float]]:
    req = urllib.request.Request(
        OLLAMA_URL,
        data=json.dumps({"model": MODEL, "input": texts}).encode(),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=300) as r:
        payload = json.load(r)
    embeddings = payload.get("embeddings") or []
    if len(embeddings) != len(texts):
        raise SystemExit(f"expected {len(texts)} embeddings, got {len(embeddings)}")
    for e in embeddings:
        if len(e) != DIMS:
            raise SystemExit(f"model returned {len(e)} dims; schema expects {DIMS}")
    return embeddings


async def run(limit: int | None, re_embed: bool) -> None:
    pool = make_pool()
    await pool.open()
    await init_schema(pool)
    try:
        async with pool.connection() as con:
            if re_embed:
                await con.execute(
                    "UPDATE document_chunks SET embedding = NULL, embedding_model = NULL"
                )
                print("cleared existing embeddings")

            done = 0
            while limit is None or done < limit:
                take = BATCH if limit is None else min(BATCH, limit - done)
                async with con.cursor() as cur:
                    await cur.execute(
                        """
                        SELECT chunk_id, content FROM document_chunks
                        WHERE embedding IS NULL ORDER BY chunk_id LIMIT %s
                        """,
                        (take,),
                    )
                    rows = await cur.fetchall()
                if not rows:
                    break

                vectors = embed_batch([r[1] for r in rows])
                for (chunk_id, _), vec in zip(rows, vectors):
                    await con.execute(
                        """
                        UPDATE document_chunks
                        SET embedding = %s::vector, embedding_model = %s
                        WHERE chunk_id = %s
                        """,
                        (json.dumps(vec), MODEL, chunk_id),
                    )
                done += len(rows)
                print(f"embedded {done} chunks…", flush=True)

            async with con.cursor() as cur:
                await cur.execute(
                    "SELECT count(*) FROM document_chunks WHERE embedding IS NULL"
                )
                pending = (await cur.fetchone())[0]
            print(f"done: {done} embedded this run, {pending} still pending")
    finally:
        await pool.close()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--limit", type=int, default=None)
    ap.add_argument("--re-embed", action="store_true",
                    help="clear all embeddings first (after a model change)")
    args = ap.parse_args()
    asyncio.run(run(args.limit, args.re_embed))


if __name__ == "__main__":
    main()
