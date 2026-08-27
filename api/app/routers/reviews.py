"""GET /reviews — the Center's authored bill reviews with live status merged.

Read-only; never calls congress.gov. Seed/author reviews with
api/seed_reviews.py (or future authoring tooling).
"""
from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, Request

from ..reviews import get_review, get_review_store

router = APIRouter()


def get_pool(request: Request):
    return request.app.state.pool


@router.get("/reviews")
async def list_reviews(pool=Depends(get_pool)) -> dict:
    async with pool.connection() as con:
        return await get_review_store(con)


@router.get("/reviews/{review_id}")
async def read_review(review_id: str, pool=Depends(get_pool)) -> dict:
    async with pool.connection() as con:
        doc = await get_review(con, review_id)
    if doc is None:
        raise HTTPException(status_code=404, detail=f"no review '{review_id}'")
    return doc
