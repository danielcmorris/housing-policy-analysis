"""Pydantic response models for the law-retrieval API."""
from __future__ import annotations

from datetime import date, datetime

from pydantic import BaseModel


class TextVersion(BaseModel):
    version_code: str
    version_name: str | None = None
    version_date: datetime | None = None
    format_type: str
    url: str | None = None
    text_content: str | None = None


class Bill(BaseModel):
    bill_id: str
    congress: int
    bill_type: str
    bill_number: int
    title: str | None = None
    origin_chamber: str | None = None
    latest_action_date: date | None = None
    latest_action_text: str | None = None
    update_date: datetime | None = None
    data_vintage: datetime
    text_versions: list[TextVersion] = []
