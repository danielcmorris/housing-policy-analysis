"""GET /bills/{congress}/{bill_type}/{bill_number} — fetch-and-store one law."""
from __future__ import annotations

from typing import AsyncIterator

from fastapi import APIRouter, Depends, HTTPException, Request

from .. import config, repository
from ..congress_client import (BillNotFound, CongressAPIError, CongressClient,
                               RateLimited)
from ..models import Bill

router = APIRouter()


def get_pool(request: Request):
    return request.app.state.pool


async def get_client() -> AsyncIterator[CongressClient]:
    """Per-request congress.gov client. Overridden in tests to inject a mock."""
    client = CongressClient(api_key=config.get_settings().congress_api_key)
    try:
        yield client
    finally:
        await client.aclose()


@router.get("/bills/{congress}/{bill_type}/{bill_number}", response_model=Bill)
async def get_law(
    congress: int,
    bill_type: str,
    bill_number: int,
    refresh: bool = False,
    pool=Depends(get_pool),
    client: CongressClient = Depends(get_client),
) -> dict:
    bill_type = bill_type.lower()
    if bill_type not in config.VALID_BILL_TYPES:
        raise HTTPException(status_code=400, detail=f"invalid bill_type '{bill_type}'")

    slug = repository.bill_slug(congress, bill_type, bill_number)

    async with pool.connection() as con:
        if not refresh:
            existing = await repository.get_bill(con, slug)
            if existing is not None:
                return existing

        try:
            bill_json = await client.fetch_bill(congress, bill_type, bill_number, refresh)
            text_json = await client.fetch_bill_text(congress, bill_type, bill_number, refresh)
            bodies: dict[str, str] = {}
            for v in repository.parse_text_versions(text_json):
                if v["format_type"] == "Formatted Text" and v["url"]:
                    bodies[v["url"]] = await client.fetch_text_body(v["url"])
        except BillNotFound:
            raise HTTPException(status_code=404, detail=f"bill {slug} not found upstream")
        except RateLimited:
            raise HTTPException(status_code=429, detail="congress.gov rate limit exceeded")
        except CongressAPIError as exc:
            raise HTTPException(status_code=502, detail=f"congress.gov error: {exc}")

        await repository.store_law(
            con, congress, bill_type, bill_number, bill_json, text_json, bodies
        )
        result = await repository.get_bill(con, slug)

    assert result is not None  # just written
    return result
