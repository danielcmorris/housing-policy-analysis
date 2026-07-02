"""
Declarative configuration + secrets resolution for the law-retrieval API.

Mirrors the "config is a registry, not logic" spirit of
analysis/minneapolis_proof/config.py, but here we also need real secrets
(the congress.gov API key and the Postgres DSN). Those are resolved by
pydantic-settings with this precedence (first hit wins):

    1. process environment variables
    2. creds/api.env   (dotenv; gitignored, referenced as @creds in CLAUDE.md)

Only URL *templates* live in this file. No secret value is ever hard-coded.
"""
from __future__ import annotations

import os
from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict

# Repo root = two levels up from this file (api/app/config.py -> repo/).
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CREDS_ENV = os.path.join(REPO_ROOT, "creds", "api.env")

# --- congress.gov endpoint registry (public, no secrets) --------------------
CONGRESS_BASE_URL = "https://api.congress.gov/v3"
# {base}/bill/{congress}/{bill_type}/{bill_number}
BILL_ENDPOINT = "{base}/bill/{congress}/{bill_type}/{bill_number}"
BILL_TEXT_ENDPOINT = "{base}/bill/{congress}/{bill_type}/{bill_number}/text"

# Provenance row written into the `sources` table.
SOURCE = {
    "source_id": "congress_gov",
    "name": "api.congress.gov v3",
    "publisher": "Library of Congress",
    "url": CONGRESS_BASE_URL,  # template/base only; never includes api_key
}

VALID_BILL_TYPES = {"hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres"}


class Settings(BaseSettings):
    """Runtime settings. Values come from env or creds/api.env."""

    model_config = SettingsConfigDict(
        env_file=CREDS_ENV,
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # Secrets / connection.
    database_url: str = "postgresql://housing:housing@localhost:5432/housing"
    congress_api_key: str = ""

    # Endpoints (overridable, mainly for tests).
    congress_base_url: str = CONGRESS_BASE_URL

    # Raw-zone disk cache for upstream JSON.
    data_dir: str = os.path.join(REPO_ROOT, "api", "data")

    # HTTP client behaviour.
    http_timeout: float = 60.0
    http_retries: int = 3


@lru_cache
def get_settings() -> Settings:
    return Settings()
