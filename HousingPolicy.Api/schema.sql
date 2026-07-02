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

-- ---------------------------------------------------------------------------
-- Bill metadata sub-resources (congress.gov: /cosponsors, /amendments, ...).
-- Each is fetched from its own endpoint and replaced wholesale on refresh.
-- Two extra inline fields on `bills` come free with the /bill payload:
ALTER TABLE bills ADD COLUMN IF NOT EXISTS introduced_date DATE;
ALTER TABLE bills ADD COLUMN IF NOT EXISTS policy_area     TEXT;

-- Bill sponsor(s) — inline in the /bill payload (usually one).
CREATE TABLE IF NOT EXISTS bill_sponsors (
    bill_id       TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    bioguide_id   TEXT NOT NULL,
    full_name     TEXT,
    first_name    TEXT,
    last_name     TEXT,
    party         TEXT,
    state         TEXT,
    district      INTEGER,
    is_by_request TEXT,
    url           TEXT,
    PRIMARY KEY (bill_id, bioguide_id)
);

CREATE TABLE IF NOT EXISTS bill_cosponsors (
    bill_id               TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    bioguide_id           TEXT NOT NULL,
    full_name             TEXT,
    party                 TEXT,
    state                 TEXT,
    district              INTEGER,
    is_original_cosponsor BOOLEAN,
    sponsorship_date      DATE,
    url                   TEXT,
    PRIMARY KEY (bill_id, bioguide_id)
);

CREATE TABLE IF NOT EXISTS bill_amendments (
    bill_id            TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    amendment_congress INTEGER,
    amendment_type     TEXT NOT NULL,        -- 'HAMDT','SAMDT'
    amendment_number   TEXT NOT NULL,
    update_date        TIMESTAMPTZ,
    url                TEXT,
    PRIMARY KEY (bill_id, amendment_type, amendment_number)
);

-- Actions carry no natural key; keep source order via `ordinal`.
CREATE TABLE IF NOT EXISTS bill_actions (
    bill_id            TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    ordinal            INTEGER NOT NULL,
    action_date        DATE,
    action_code        TEXT,
    action_type        TEXT,
    source_system_code INTEGER,
    source_system_name TEXT,
    text               TEXT,
    PRIMARY KEY (bill_id, ordinal)
);

CREATE TABLE IF NOT EXISTS bill_committees (
    bill_id     TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    system_code TEXT NOT NULL,
    chamber     TEXT,
    name        TEXT,
    type        TEXT,
    url         TEXT,
    activities  JSONB,                        -- [{date,name}, ...]
    PRIMARY KEY (bill_id, system_code)
);

CREATE TABLE IF NOT EXISTS bill_subjects (
    bill_id     TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    name        TEXT NOT NULL,               -- legislative subject term
    update_date TIMESTAMPTZ,
    PRIMARY KEY (bill_id, name)
);

CREATE TABLE IF NOT EXISTS bill_summaries (
    bill_id      TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    version_code TEXT NOT NULL,              -- CRS summary version, e.g. '00','07'
    action_date  DATE,
    action_desc  TEXT,
    update_date  TIMESTAMPTZ,
    text         TEXT,                       -- HTML summary body
    PRIMARY KEY (bill_id, version_code)
);

CREATE TABLE IF NOT EXISTS bill_titles (
    bill_id                TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    ordinal                INTEGER NOT NULL,
    title                  TEXT,
    title_type             TEXT,
    title_type_code        INTEGER,
    bill_text_version_code TEXT,
    chamber_code           TEXT,
    chamber_name           TEXT,
    update_date            TIMESTAMPTZ,
    PRIMARY KEY (bill_id, ordinal)
);

CREATE TABLE IF NOT EXISTS bill_related_bills (
    bill_id             TEXT NOT NULL REFERENCES bills(bill_id) ON DELETE CASCADE,
    related_congress    INTEGER NOT NULL,
    related_type        TEXT NOT NULL,       -- 'HR','S',...
    related_number      INTEGER NOT NULL,
    title               TEXT,
    latest_action_date  DATE,
    latest_action_text  TEXT,
    relationship_details JSONB,              -- [{identifiedBy,type}, ...]
    url                 TEXT,
    PRIMARY KEY (bill_id, related_congress, related_type, related_number)
);

CREATE INDEX IF NOT EXISTS idx_cosponsors_member ON bill_cosponsors (bioguide_id);
CREATE INDEX IF NOT EXISTS idx_subjects_name ON bill_subjects (name);
