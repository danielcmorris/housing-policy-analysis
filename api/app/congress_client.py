"""
Thin async client for api.congress.gov v3.

Responsibility is narrow on purpose (same split as the proof slice, where
`_get()` only fetches and `fetch_load.py` does the shaping): this module
fetches raw JSON / text and returns it. Normalization into the DB lives in
repository.py.

Adds what the proof's `_get` lacked but a keyed, rate-limited API needs:
retry with exponential backoff (honoring Retry-After), typed errors, and
api_key redaction so the secret never reaches a log, exception, or cache.
"""
from __future__ import annotations

import asyncio
import json
import os
import urllib.parse

import httpx

from . import config


# --- typed errors -----------------------------------------------------------
class CongressAPIError(Exception):
    """Non-recoverable upstream error (unexpected status, malformed body)."""


class BillNotFound(CongressAPIError):
    """The bill does not exist upstream (HTTP 404)."""


class RateLimited(CongressAPIError):
    """Rate limit not cleared after retries (HTTP 429)."""


def redact(url: str) -> str:
    """Return url with any api_key query value masked."""
    parts = urllib.parse.urlsplit(url)
    if "api_key" not in parts.query:
        return url
    q = urllib.parse.parse_qsl(parts.query, keep_blank_values=True)
    q = [(k, "REDACTED" if k == "api_key" else v) for k, v in q]
    return urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(q)))


class CongressClient:
    def __init__(
        self,
        api_key: str,
        *,
        base_url: str | None = None,
        data_dir: str | None = None,
        timeout: float | None = None,
        retries: int | None = None,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        s = config.get_settings()
        self.api_key = api_key
        self.base_url = (base_url or s.congress_base_url).rstrip("/")
        self.data_dir = data_dir or s.data_dir
        self.retries = s.http_retries if retries is None else retries
        self._owns_client = client is None
        self._client = client or httpx.AsyncClient(
            timeout=s.http_timeout if timeout is None else timeout,
            headers={"User-Agent": "housing-policy-laws/0.1"},
        )

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def __aenter__(self) -> "CongressClient":
        return self

    async def __aexit__(self, *exc) -> None:
        await self.aclose()

    # --- low-level request with retry --------------------------------------
    async def _request(self, url: str, params: dict) -> httpx.Response:
        """GET with retry on timeout / network error / 429 / 5xx.

        404 is permanent and raised immediately as BillNotFound.
        """
        last_exc: Exception | None = None
        for attempt in range(self.retries + 1):
            try:
                resp = await self._client.get(url, params=params)
            except (httpx.TimeoutException, httpx.TransportError) as exc:
                last_exc = exc
            else:
                if resp.status_code == 404:
                    raise BillNotFound(f"not found: {redact(str(resp.request.url))}")
                if resp.status_code < 400:
                    return resp
                if resp.status_code == 429 or resp.status_code >= 500:
                    last_exc = CongressAPIError(
                        f"HTTP {resp.status_code} from {redact(str(resp.request.url))}"
                    )
                    await self._sleep_before_retry(attempt, resp)
                    continue
                # other 4xx: permanent
                raise CongressAPIError(
                    f"HTTP {resp.status_code} from {redact(str(resp.request.url))}: "
                    f"{resp.text[:200]}"
                )
            await self._sleep_before_retry(attempt, None)

        # retries exhausted
        if isinstance(last_exc, CongressAPIError) and "429" in str(last_exc):
            raise RateLimited(str(last_exc))
        raise CongressAPIError(f"request failed after {self.retries} retries: {last_exc}")

    async def _sleep_before_retry(self, attempt: int, resp: httpx.Response | None) -> None:
        if attempt >= self.retries:
            return
        backoff = 0.5 * (2 ** attempt)
        if resp is not None:
            retry_after = resp.headers.get("Retry-After")
            if retry_after and retry_after.isdigit():
                backoff = max(backoff, float(retry_after))
        await asyncio.sleep(backoff)

    # --- JSON endpoints with raw-zone disk cache ---------------------------
    async def _get_json(self, url: str, cache_path: str, refresh: bool) -> dict:
        if not refresh and os.path.exists(cache_path):
            with open(cache_path, encoding="utf-8") as f:
                return json.load(f)
        params = {"format": "json", "api_key": self.api_key}
        resp = await self._request(url, params)
        try:
            data = resp.json()
        except ValueError as exc:
            raise CongressAPIError(f"non-JSON response from {redact(str(resp.request.url))}") from exc
        os.makedirs(os.path.dirname(cache_path), exist_ok=True)
        with open(cache_path, "w", encoding="utf-8") as f:
            json.dump(data, f)
        return data

    async def fetch_bill(self, congress: int, bill_type: str, bill_number: int,
                         refresh: bool = False) -> dict:
        url = config.BILL_ENDPOINT.format(
            base=self.base_url, congress=congress, bill_type=bill_type, bill_number=bill_number
        )
        cache = os.path.join(self.data_dir, f"bill_{congress}_{bill_type}_{bill_number}.json")
        return await self._get_json(url, cache, refresh)

    async def fetch_bill_text(self, congress: int, bill_type: str, bill_number: int,
                              refresh: bool = False) -> dict:
        url = config.BILL_TEXT_ENDPOINT.format(
            base=self.base_url, congress=congress, bill_type=bill_type, bill_number=bill_number
        )
        cache = os.path.join(self.data_dir, f"bill_{congress}_{bill_type}_{bill_number}_text.json")
        return await self._get_json(url, cache, refresh)

    async def fetch_text_body(self, url: str) -> str:
        """Fetch a text-version body (public govinfo/congress.gov URL, no api_key)."""
        resp = await self._request(url, params={})
        return resp.text
