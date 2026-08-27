"""
FastAPI app for the Housing Policy law corpus.

Step 1 exposes a single retrieval endpoint plus /health. The front-end phase
will add endpoints to this same app (per CLAUDE.md). Schema is applied on
startup so a fresh Postgres boots ready.
"""
from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from .db import init_schema, make_pool
from .routers import admin, bills, legislation, reviews


@asynccontextmanager
async def lifespan(app: FastAPI):
    pool = make_pool()
    await pool.open()
    await init_schema(pool)
    app.state.pool = pool
    try:
        yield
    finally:
        await pool.close()


app = FastAPI(title="Housing Policy — Law Retrieval API", version="0.1.0", lifespan=lifespan)
app.include_router(bills.router)
app.include_router(legislation.router)
app.include_router(admin.router)
app.include_router(reviews.router)

# The Angular dev server (ng serve) runs on :4200; the /admin pages POST.
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:4200", "http://127.0.0.1:4200"],
    allow_methods=["GET", "POST"],
    allow_headers=["*"],
)


@app.get("/health")
async def health() -> dict:
    async with app.state.pool.connection() as con:
        await con.execute("SELECT 1")
    return {"status": "ok"}
