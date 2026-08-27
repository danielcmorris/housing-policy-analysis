"""Admin endpoints for curating the legislation tracker.

Consumed by the Angular /admin pages. These DO call congress.gov (refresh,
discover, add, track) — each run is bounded by the sync caps in config.
"""
from __future__ import annotations

from typing import AsyncIterator

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from pydantic import BaseModel

from .. import config, legislation
from ..congress_client import CongressAPIError, CongressClient, RateLimited

router = APIRouter(prefix="/admin")


def get_pool(request: Request):
    return request.app.state.pool


async def get_client() -> AsyncIterator[CongressClient]:
    client = CongressClient(api_key=config.get_settings().congress_api_key)
    try:
        yield client
    finally:
        await client.aclose()


def _upstream_errors(exc: Exception) -> HTTPException:
    if isinstance(exc, RateLimited):
        return HTTPException(status_code=429, detail="congress.gov rate limit exceeded")
    return HTTPException(status_code=502, detail=f"congress.gov error: {exc}")


@router.get("/stats")
async def stats(pool=Depends(get_pool)) -> dict:
    async with pool.connection() as con:
        async with con.cursor() as cur:
            await cur.execute(
                """
                SELECT tracking_status, count(*),
                       count(*) FILTER (WHERE EXISTS (
                           SELECT 1 FROM bill_text_versions tv
                           WHERE tv.bill_id = b.bill_id AND tv.text_content IS NOT NULL))
                FROM bills b WHERE policy_area = %s
                GROUP BY tracking_status
                """,
                (config.HOUSING_POLICY_AREA,),
            )
            counts = {r[0]: {"bills": r[1], "with_text": r[2]} for r in await cur.fetchall()}
        last_refresh = await legislation.get_sync_state(con, "last_refresh_at")
        last_discovery = await legislation.get_sync_state(con, "last_discovery_at")
    return {
        "tracked": counts.get("tracked", {"bills": 0, "with_text": 0}),
        "untracked": counts.get("untracked", {"bills": 0, "with_text": 0}),
        "last_refresh_at": last_refresh,
        "last_discovery_at": last_discovery,
    }


@router.post("/refresh")
async def refresh(
    congress: int = 119,
    pool=Depends(get_pool),
    client: CongressClient = Depends(get_client),
) -> dict:
    async with pool.connection() as con:
        try:
            return await legislation.refresh_tracked(con, client, congress)
        except CongressAPIError as exc:
            raise _upstream_errors(exc)


@router.post("/discover")
async def discover(
    congress: int = 119,
    days: int = Query(default=30, ge=1, le=365),
    pool=Depends(get_pool),
    client: CongressClient = Depends(get_client),
) -> dict:
    async with pool.connection() as con:
        try:
            return await legislation.discover_new(con, client, congress, days)
        except CongressAPIError as exc:
            raise _upstream_errors(exc)


class AddBillRequest(BaseModel):
    congress: int
    bill_type: str
    bill_number: int
    tracked: bool


@router.post("/bills")
async def add_bill(
    body: AddBillRequest,
    pool=Depends(get_pool),
    client: CongressClient = Depends(get_client),
) -> dict:
    bill_type = body.bill_type.lower()
    if bill_type not in config.VALID_BILL_TYPES:
        raise HTTPException(status_code=400, detail=f"invalid bill_type '{bill_type}'")
    async with pool.connection() as con:
        try:
            slug = await legislation.add_bill(
                con, client, body.congress, bill_type, body.bill_number, tracked=body.tracked
            )
        except CongressAPIError as exc:
            raise _upstream_errors(exc)
    if slug is None:
        raise HTTPException(status_code=404, detail="bill not found upstream")
    return {"bill_id": slug, "tracking_status": "tracked" if body.tracked else "untracked"}


@router.post("/bills/{bill_id}/refresh")
async def refresh_bill(
    bill_id: str,
    pool=Depends(get_pool),
    client: CongressClient = Depends(get_client),
) -> dict:
    async with pool.connection() as con:
        try:
            result = await legislation.refresh_one(con, client, bill_id)
        except CongressAPIError as exc:
            raise _upstream_errors(exc)
    if result is None:
        raise HTTPException(status_code=404, detail=f"unknown bill_id '{bill_id}'")
    return result


class DisplayRequest(BaseModel):
    displayed: bool


@router.post("/bills/{bill_id}/display")
async def set_display(
    bill_id: str,
    body: DisplayRequest,
    pool=Depends(get_pool),
) -> dict:
    async with pool.connection() as con:
        result = await legislation.set_display(con, bill_id, body.displayed)
    if result is None:
        raise HTTPException(status_code=404, detail=f"unknown bill_id '{bill_id}'")
    return result


class PinRequest(BaseModel):
    pinned: bool


@router.post("/bills/{bill_id}/pin")
async def set_pinned(
    bill_id: str,
    body: PinRequest,
    pool=Depends(get_pool),
) -> dict:
    async with pool.connection() as con:
        result = await legislation.set_pinned(con, bill_id, body.pinned)
    if result is None:
        raise HTTPException(status_code=404, detail=f"unknown bill_id '{bill_id}'")
    return result


class TrackingRequest(BaseModel):
    tracked: bool


@router.post("/bills/{bill_id}/tracking")
async def set_tracking(
    bill_id: str,
    body: TrackingRequest,
    pool=Depends(get_pool),
    client: CongressClient = Depends(get_client),
) -> dict:
    async with pool.connection() as con:
        try:
            result = await legislation.set_tracking(con, client, bill_id, tracked=body.tracked)
        except CongressAPIError as exc:
            raise _upstream_errors(exc)
    if result is None:
        raise HTTPException(status_code=404, detail=f"unknown bill_id '{bill_id}'")
    return result
