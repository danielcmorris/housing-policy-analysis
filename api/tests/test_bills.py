"""
Offline tests for the law-retrieval API.

Pure-parse and client tests run with no network and no DB (httpx MockTransport).
The end-to-end test additionally needs a reachable Postgres (docker-compose up
postgres); it is skipped automatically when the DB is unreachable.

Async client code is exercised via asyncio.run() so we need no pytest plugins.
"""
from __future__ import annotations

import asyncio
import json
import os

import httpx
import psycopg
import pytest
from fastapi.testclient import TestClient

from api.app import config, db, repository
from api.app.congress_client import BillNotFound, CongressClient, RateLimited, redact


async def _no_sleep(*_a, **_k):
    """Drop-in for asyncio.sleep that never actually waits (and never recurses)."""
    return None

REPO_ROOT = config.REPO_ROOT
FIXTURES = os.path.join(REPO_ROOT, "api", "fixtures")
HR6644_TXT = os.path.join(REPO_ROOT, "documents", "HR6644-119-enrolled.txt")

BILL_JSON = json.load(open(os.path.join(FIXTURES, "bill_119_hr_6644.json")))
TEXT_JSON = json.load(open(os.path.join(FIXTURES, "bill_119_hr_6644_text.json")))
BODY_TEXT = open(HR6644_TXT, encoding="utf-8", errors="replace").read()


# --- pure helpers -----------------------------------------------------------
def test_redact_masks_key():
    url = "https://api.congress.gov/v3/bill/119/hr/6644?format=json&api_key=SECRET123"
    out = redact(url)
    assert "SECRET123" not in out
    assert "api_key=REDACTED" in out


def test_parse_bill():
    b = repository.parse_bill(BILL_JSON)
    assert b["congress"] == 119
    assert b["bill_type"] == "hr"
    assert b["bill_number"] == 6644
    assert b["title"] == "21st Century ROAD to Housing Act"
    assert b["origin_chamber"] == "House"
    assert b["latest_action_date"] == "2026-01-15"


def test_parse_text_versions_and_code():
    versions = repository.parse_text_versions(TEXT_JSON)
    codes = {(v["version_code"], v["format_type"]) for v in versions}
    assert ("enr", "Formatted Text") in codes
    assert ("enr", "PDF") in codes


# --- client behaviour with a mocked transport -------------------------------
def _client(handler, **kw) -> CongressClient:
    transport = httpx.MockTransport(handler)
    http = httpx.AsyncClient(transport=transport)
    return CongressClient(api_key="SECRET123", data_dir=kw.pop("data_dir", "/tmp/nocache-xyz"),
                          client=http, **kw)


def test_fetch_bill_404_raises_billnotfound(tmp_path):
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(404, text="not found")

    async def go():
        c = _client(handler, data_dir=str(tmp_path))
        try:
            with pytest.raises(BillNotFound) as ei:
                await c.fetch_bill(119, "hr", 999999, refresh=True)
            assert "SECRET123" not in str(ei.value)  # redacted
        finally:
            await c.aclose()

    asyncio.run(go())


def test_retry_on_429_then_success(tmp_path, monkeypatch):
    calls = {"n": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        calls["n"] += 1
        if calls["n"] < 3:
            return httpx.Response(429, headers={"Retry-After": "0"}, text="slow down")
        return httpx.Response(200, json=BILL_JSON)

    async def go():
        # no real waiting
        monkeypatch.setattr(asyncio, "sleep", _no_sleep)
        c = _client(handler, data_dir=str(tmp_path), retries=3)
        try:
            data = await c.fetch_bill(119, "hr", 6644, refresh=True)
            assert data["bill"]["number"] == "6644"
            assert calls["n"] == 3
        finally:
            await c.aclose()

    asyncio.run(go())


def test_rate_limited_when_retries_exhausted(tmp_path, monkeypatch):
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(429, headers={"Retry-After": "0"}, text="slow down")

    async def go():
        monkeypatch.setattr(asyncio, "sleep", _no_sleep)
        c = _client(handler, data_dir=str(tmp_path), retries=2)
        try:
            with pytest.raises(RateLimited):
                await c.fetch_bill(119, "hr", 6644, refresh=True)
        finally:
            await c.aclose()

    asyncio.run(go())


def test_disk_cache_hit_skips_network(tmp_path):
    calls = {"n": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        calls["n"] += 1
        return httpx.Response(200, json=BILL_JSON)

    async def go():
        c = _client(handler, data_dir=str(tmp_path))
        try:
            await c.fetch_bill(119, "hr", 6644)          # miss -> 1 call, writes cache
            await c.fetch_bill(119, "hr", 6644)          # hit  -> no new call
            assert calls["n"] == 1
        finally:
            await c.aclose()

    asyncio.run(go())


# --- end-to-end through the FastAPI app (needs Postgres) --------------------
def _db_available() -> bool:
    try:
        with psycopg.connect(config.get_settings().database_url, connect_timeout=2) as con:
            con.execute("SELECT 1")
        return True
    except Exception:
        return False


requires_db = pytest.mark.skipif(not _db_available(), reason="Postgres not reachable")


def _mock_app_client(counter: dict):
    """A CongressClient serving fixtures, counting upstream bill fetches."""
    def handler(request: httpx.Request) -> httpx.Response:
        path = request.url.path
        if path.endswith(".htm"):
            return httpx.Response(200, text=BODY_TEXT)
        if path.endswith("/text"):
            return httpx.Response(200, json=TEXT_JSON)
        if "/bill/119/hr/6644" in path:
            counter["bill"] = counter.get("bill", 0) + 1
            return httpx.Response(200, json=BILL_JSON)
        return httpx.Response(404, text="not found")

    http = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    return CongressClient(api_key="SECRET123", client=http)


@requires_db
def test_get_law_end_to_end(tmp_path):
    from api.app.main import app
    from api.app.routers.bills import get_client

    # ensure schema exists, then clean any prior run
    with open(db.SCHEMA_PATH, encoding="utf-8") as f:
        ddl = f.read()
    with psycopg.connect(config.get_settings().database_url) as con:
        con.execute(ddl)
        con.execute("DELETE FROM raw_payloads WHERE bill_id='119-hr-6644'")
        con.execute("DELETE FROM bill_text_versions WHERE bill_id='119-hr-6644'")
        con.execute("DELETE FROM bills WHERE bill_id='119-hr-6644'")
        con.commit()

    counter: dict = {}

    async def override():
        c = _mock_app_client(counter)
        # keep bodies out of the shared data_dir cache
        c.data_dir = str(tmp_path)
        try:
            yield c
        finally:
            await c.aclose()

    app.dependency_overrides[get_client] = override
    try:
        with TestClient(app) as tc:
            r = tc.get("/bills/119/hr/6644")
            assert r.status_code == 200, r.text
            body = r.json()
            assert body["bill_id"] == "119-hr-6644"
            assert body["title"] == "21st Century ROAD to Housing Act"
            enr = [v for v in body["text_versions"]
                   if v["version_code"] == "enr" and v["format_type"] == "Formatted Text"]
            assert enr and enr[0]["text_content"]
            assert "ROAD to Housing Act" in enr[0]["text_content"]

            # second call, no refresh -> served from DB, no new upstream bill fetch
            r2 = tc.get("/bills/119/hr/6644")
            assert r2.status_code == 200
            assert counter["bill"] == 1

            # unknown bill -> 404
            assert tc.get("/bills/119/hr/999999").status_code == 404
            # invalid type -> 400
            assert tc.get("/bills/119/zz/1").status_code == 400
    finally:
        app.dependency_overrides.clear()


@requires_db
def test_api_key_never_persisted(tmp_path):
    """The api_key must not land in the DB (payloads store responses, not requests)."""
    with psycopg.connect(config.get_settings().database_url) as con:
        rows = con.execute(
            "SELECT payload_json::text FROM raw_payloads WHERE bill_id='119-hr-6644'"
        ).fetchall()
    for (txt,) in rows:
        assert "SECRET123" not in txt
        assert "api_key" not in txt
