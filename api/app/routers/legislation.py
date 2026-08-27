"""GET /legislation — the housing-legislation tracker feed.

Serves rows maintained by the legislation sync (api/sync_legislation.py).
Read-only: this endpoint never calls congress.gov.
"""
from __future__ import annotations

from fastapi import APIRouter, Depends, Query, Request

from .. import config
from ..legislation import list_tracker

router = APIRouter()


def get_pool(request: Request):
    return request.app.state.pool


@router.get("/legislation")
async def get_legislation(
    congress: int | None = None,
    policy_area: str = config.HOUSING_POLICY_AREA,
    view: str = Query(default="public", pattern="^(public|admin)$"),
    tracking: str = Query(default="all", pattern="^(tracked|untracked|all)$"),
    q: str | None = None,
    limit: int = Query(default=50, le=200),
    pool=Depends(get_pool),
) -> list[dict]:
    async with pool.connection() as con:
        return await list_tracker(
            con, policy_area=policy_area, congress=congress, limit=limit,
            view=view, tracking=tracking, q=q,
        )
