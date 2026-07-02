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
