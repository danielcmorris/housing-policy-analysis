-- Serving-layer schema for the housing-policy law corpus (federal, step 1).
-- Postgres-native (this is the real target now, not SQLite).
--
-- Design principle carried over from analysis/minneapolis_proof/schema.sql:
-- every stored fact points at a `sources` row and carries a retrieval stamp,
-- so any record is self-citing. Raw upstream JSON is mirrored verbatim in
-- `raw_payloads` (the raw-zone) so we can re-parse without re-fetching.

CREATE TABLE IF NOT EXISTS sources (
    source_id TEXT PRIMARY KEY,          -- 'congress_gov'
    name      TEXT NOT NULL,
    publisher TEXT,                      -- 'Library of Congress'
    url       TEXT                       -- base URL TEMPLATE, never carries an api_key
);

-- One row per individual bill/law, keyed by a readable slug.
CREATE TABLE IF NOT EXISTS bills (
    bill_id            TEXT PRIMARY KEY,           -- '119-hr-6644'  (congress-type-number)
    congress           INTEGER NOT NULL,
    bill_type          TEXT NOT NULL,              -- 'hr','s','hjres',...
    bill_number        INTEGER NOT NULL,
    title              TEXT,
    origin_chamber     TEXT,                       -- 'House' / 'Senate'
    latest_action_date DATE,
    latest_action_text TEXT,
    update_date        TIMESTAMPTZ,                -- congress.gov updateDate; drives "is my copy stale?"
    source_id          TEXT NOT NULL REFERENCES sources(source_id),
    data_vintage       TIMESTAMPTZ NOT NULL,       -- when WE retrieved it
    UNIQUE (congress, bill_type, bill_number)
);

-- /bill/.../text returns one entry per version (enrolled, introduced, ...),
-- each with several format links. We keep the Formatted-Text body inline.
CREATE TABLE IF NOT EXISTS bill_text_versions (
    bill_id      TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    version_code TEXT NOT NULL,          -- normalized short code: 'enr','ih','rh',... ('' if unknown)
    version_name TEXT,                   -- 'Enrolled Bill'
    version_date TIMESTAMPTZ,
    format_type  TEXT NOT NULL,          -- 'Formatted Text','PDF','XML'
    url          TEXT,                   -- upstream body URL
    text_content TEXT,                   -- full body (fetched for Formatted Text)
    PRIMARY KEY (bill_id, version_code, format_type)
);

-- Immutable raw-zone mirror of every upstream response (audit + reparse).
CREATE TABLE IF NOT EXISTS raw_payloads (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    bill_id      TEXT NOT NULL,
    endpoint     TEXT NOT NULL,          -- 'bill','text',...
    fetched_at   TIMESTAMPTZ NOT NULL,
    http_status  INTEGER,
    payload_json JSONB NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_bills_update ON bills (update_date);
CREATE INDEX IF NOT EXISTS idx_raw_bill ON raw_payloads (bill_id, endpoint);

-- Legislation-tracker columns (already present on the provisioned policydb;
-- these ALTERs bring a fresh local dev DB to parity).
ALTER TABLE bills ADD COLUMN IF NOT EXISTS introduced_date DATE;
ALTER TABLE bills ADD COLUMN IF NOT EXISTS policy_area TEXT;
CREATE INDEX IF NOT EXISTS idx_bills_policy_area ON bills (policy_area, latest_action_date);

-- Sponsor(s) of a bill, from the bill detail payload. Column set matches the
-- provisioned policydb table. Rows are replaced wholesale per bill on sync
-- (delete + insert), so no unique constraint is required.
CREATE TABLE IF NOT EXISTS bill_sponsors (
    bill_id       TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    bioguide_id   TEXT,
    full_name     TEXT,
    first_name    TEXT,
    last_name     TEXT,
    party         TEXT,
    state         TEXT,
    district      INTEGER,
    is_by_request TEXT,
    url           TEXT
);
CREATE INDEX IF NOT EXISTS idx_bill_sponsors_bill ON bill_sponsors (bill_id);

-- CRS-written summaries from /bill/.../summaries (one row per version).
-- Replaced wholesale per bill on sync.
CREATE TABLE IF NOT EXISTS bill_summaries (
    bill_id      TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    version_code TEXT,
    action_date  DATE,
    action_desc  TEXT,
    update_date  TIMESTAMPTZ,
    text         TEXT
);
CREATE INDEX IF NOT EXISTS idx_bill_summaries_bill ON bill_summaries (bill_id);

-- Tracker curation: 'tracked' bills carry full text and appear on the public
-- tracker; 'untracked' bills are known (metadata + summary) but not followed.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS tracking_status TEXT NOT NULL DEFAULT 'tracked';

-- Topic tags, auto-derived by scanning the CRS summary the first time it is
-- ingested (see legislation.derive_tags). Never overwritten on re-sync, so
-- manual curation of this column is safe.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS tags TEXT[] NOT NULL DEFAULT '{}';
-- Where the tags came from: 'summary' (CRS summary scan — final), 'title'
-- (provisional scan of title/latest action, upgraded when a summary arrives),
-- or 'manual'. Summary scans only ever replace NULL/'title' tags.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS tags_source TEXT;

-- Public visibility is decided by display_date, NOT by tracking_status:
-- a bill appears on the public tracker when display_date IS NOT NULL and
-- display_date <= now(). (Future dates enable scheduling later.) Tracking
-- controls data collection; display controls publication.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS display_date TIMESTAMPTZ;

-- Pinned bills sort to the top of the public tracker.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS pinned BOOLEAN NOT NULL DEFAULT FALSE;

-- Small key/value state for sync bookkeeping (e.g. last discovery run).
CREATE TABLE IF NOT EXISTS sync_state (
    key        TEXT PRIMARY KEY,
    value      TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- The Center's authored bill reviews (the four-stage analysis rendered at
-- /bills/{review_id}). The document is JSONB shaped by
-- ui/design_handoff_policy_platform/prototype/data/bill-review.schema.json.
-- Live legislative status is merged in from `bills` at read time, so the
-- editorial document never goes stale on status.
CREATE TABLE IF NOT EXISTS bill_reviews (
    review_id  TEXT PRIMARY KEY,          -- front-end route id, e.g. 'hr6644-119'
    bill_id    TEXT UNIQUE REFERENCES bills(bill_id) ON DELETE CASCADE,
    review     JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
