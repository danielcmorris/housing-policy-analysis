-- Serving-layer schema for the housing-policy law corpus (federal, step 1).
-- Postgres-native (this is the real target now, not SQLite).
--
-- Design principle carried over from analysis/minneapolis_proof/schema.sql:
-- every stored fact points at a `sources` row and carries a retrieval stamp,
-- so any record is self-citing. Raw upstream JSON is mirrored verbatim in
-- `raw_payloads` (the raw-zone) so we can re-parse without re-fetching.

-- pgvector for the RAG chunk embeddings (document_chunks.embedding).
CREATE EXTENSION IF NOT EXISTS vector;

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
    text         TEXT,                       -- summary body, stored as plain text (HTML stripped)
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

-- ---------------------------------------------------------------------------
-- Legislation tracker curation (see Services/TrackerService.cs).

-- 'tracked' bills carry full text; 'untracked' are known but not followed.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS tracking_status TEXT NOT NULL DEFAULT 'tracked';

-- Topic tags, auto-derived by scanning the CRS summary the first time it is
-- ingested (TrackerRules.DeriveTags). tags_source records provenance:
-- 'summary' (final), 'title' (provisional, upgraded when a summary arrives),
-- or 'manual'. Summary scans only ever replace NULL/'title' tags.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS tags TEXT[] NOT NULL DEFAULT '{}';
ALTER TABLE bills ADD COLUMN IF NOT EXISTS tags_source TEXT;

-- Public visibility is decided by display_date, NOT tracking_status: a bill
-- appears on the public tracker when display_date IS NOT NULL and <= now()
-- (future dates enable scheduling). Pinned bills sort to the top.
ALTER TABLE bills ADD COLUMN IF NOT EXISTS display_date TIMESTAMPTZ;
ALTER TABLE bills ADD COLUMN IF NOT EXISTS pinned BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_bills_policy_area ON bills (policy_area, latest_action_date);

-- Small key/value state for sync bookkeeping (last refresh / discovery run).
CREATE TABLE IF NOT EXISTS sync_state (
    key        TEXT PRIMARY KEY,
    value      TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- Studies & policy proposals (see Services/StudyService.cs). These are added
-- manually — there is no upstream API. Metadata + summary live in typed
-- columns; the extracted document text is stored inline for search/AI use;
-- the PDF itself lives on disk under Studies:DocumentsDir (a bucket later).
CREATE TABLE IF NOT EXISTS studies (
    ref          TEXT PRIMARY KEY,          -- 'CUHPR-2026-0142'
    doc_type     TEXT NOT NULL DEFAULT 'study',  -- 'study' | 'proposal'
    title        TEXT NOT NULL,
    category     TEXT,                      -- research program, e.g. 'Rent Control'
    authors      TEXT,
    year         INTEGER,
    pages        INTEGER,
    status       TEXT NOT NULL DEFAULT 'Submitted',  -- review pipeline label
    clarity      NUMERIC(3,1),              -- AI-assessed 0-10, null until scored
    summary      TEXT,                      -- abstract / excerpt
    key_findings TEXT[] NOT NULL DEFAULT '{}',
    methodology  TEXT,
    text_content TEXT,                      -- extracted plain text of the document
    pdf_path     TEXT,                      -- relative path under Studies:DocumentsDir
    display_date TIMESTAMPTZ,               -- public when set and <= now() (same rule as bills)
    pinned       BOOLEAN NOT NULL DEFAULT FALSE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_studies_display ON studies (display_date, year);

-- ---------------------------------------------------------------------------
-- City legislation (see Services/CityService.cs), synced from Granicus
-- Legistar (webapi.legistar.com/v1/{client}) the same way federal bills sync
-- from congress.gov. Legistar's shape differs enough from the federal bill
-- record to warrant its own table; curation columns (tracking/display/pin/
-- tags) mirror `bills` exactly.
CREATE TABLE IF NOT EXISTS city_matters (
    city_matter_id  TEXT PRIMARY KEY,        -- '{client}-{matter_id}', e.g. 'sfgov-34028'
    client          TEXT NOT NULL,           -- legistar client key ('sfgov')
    city_name       TEXT,                    -- display name ('San Francisco')
    matter_id       INTEGER NOT NULL,        -- Legistar MatterId (internal key; gateway.aspx?M=L&ID={matter_id} is the public link)
    matter_file     TEXT,                    -- human file number ('181159')
    matter_type     TEXT,                    -- Ordinance | Resolution | ...
    title           TEXT,                    -- MatterTitle (long descriptive title)
    matter_name     TEXT,                    -- MatterName (short name, often null)
    status          TEXT,                    -- Legistar status text ('Pending Committee Action')
    body_name       TEXT,                    -- current body ('Land Use and Transportation Committee')
    intro_date      DATE,
    agenda_date     DATE,
    passed_date     DATE,
    enactment_number TEXT,
    last_modified   TIMESTAMPTZ,             -- MatterLastModifiedUtc; drives staleness
    text_content    TEXT,                    -- current matter text (plain)
    tracking_status TEXT NOT NULL DEFAULT 'tracked',
    tags            TEXT[] NOT NULL DEFAULT '{}',
    tags_source     TEXT,
    display_date    TIMESTAMPTZ,
    pinned          BOOLEAN NOT NULL DEFAULT FALSE,
    data_vintage    TIMESTAMPTZ NOT NULL,
    UNIQUE (client, matter_id)
);
CREATE INDEX IF NOT EXISTS idx_city_matters_client ON city_matters (client, last_modified);
CREATE INDEX IF NOT EXISTS idx_city_matters_display ON city_matters (display_date, intro_date);

-- ---------------------------------------------------------------------------
-- Unified document registry + RAG layer (see Services/DocumentRegistryService.cs).
--
-- `documents` is the polymorphic anchor: one row per logical document across
-- every corpus — federal bills, city matters, studies, and future state
-- bills. source_type + source_key point at the native table.
CREATE TABLE IF NOT EXISTS documents (
    document_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_type TEXT NOT NULL,               -- 'federal_bill' | 'city_matter' | 'study' | 'state_bill'
    source_key  TEXT NOT NULL,               -- bills.bill_id / city_matters.city_matter_id / studies.ref
    title       TEXT,
    jurisdiction TEXT,                       -- 'US' | 'San Francisco, CA' | publisher for studies
    doc_year    INTEGER,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (source_type, source_key)
);

-- Canonical tags, shared across every corpus, linked many-to-many.
-- (bills.tags / city_matters.tags stay as denormalized copies for the
-- tracker UI; document_tags is the canonical relation going forward.)
CREATE TABLE IF NOT EXISTS tags (
    tag_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name   TEXT UNIQUE NOT NULL
);

CREATE TABLE IF NOT EXISTS document_tags (
    document_id BIGINT NOT NULL REFERENCES documents(document_id) ON DELETE CASCADE,
    tag_id      BIGINT NOT NULL REFERENCES tags(tag_id) ON DELETE CASCADE,
    PRIMARY KEY (document_id, tag_id)
);
CREATE INDEX IF NOT EXISTS idx_document_tags_tag ON document_tags (tag_id);

-- Chunked text of every document, one row per chunk, with a pgvector
-- embedding column for RAG. Embeddings are populated by a later embedding
-- pass (embedding_model records which model produced them); the dimensionless
-- `vector` type is intentional until the model is chosen — an ANN index is
-- added then. Filterable RAG = join documents (source_type/jurisdiction/year)
-- and document_tags before the vector distance.
CREATE TABLE IF NOT EXISTS document_chunks (
    chunk_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    document_id     BIGINT NOT NULL REFERENCES documents(document_id) ON DELETE CASCADE,
    chunk_index     INTEGER NOT NULL,
    content         TEXT NOT NULL,
    token_estimate  INTEGER,                 -- rough length/4 heuristic
    embedding       vector(768),             -- nomic-embed-text (Ollama); NULL until embedded
    embedding_model TEXT,
    UNIQUE (document_id, chunk_index)
);
CREATE INDEX IF NOT EXISTS idx_document_chunks_doc ON document_chunks (document_id);

-- Upgrade a pre-typed (dimensionless) embedding column in place, then index.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'document_chunks' AND column_name = 'embedding')
       AND (SELECT atttypmod FROM pg_attribute
            WHERE attrelid = 'document_chunks'::regclass AND attname = 'embedding') = -1 THEN
        ALTER TABLE document_chunks ALTER COLUMN embedding TYPE vector(768);
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS idx_document_chunks_embedding
    ON document_chunks USING hnsw (embedding vector_cosine_ops);

-- Curated cross-corpus relationships between registry documents. Seeded by
-- DocumentRegistryService.RebuildAsync from congress.gov related-bill data
-- and the editorial precedentRefs inside bill_reviews; 'manual' rows come
-- from future admin linking. Queried in BOTH directions (a row links its
-- pair symmetrically). Embedding similarity is computed live and never
-- stored here.
CREATE TABLE IF NOT EXISTS document_relations (
    from_document_id BIGINT NOT NULL REFERENCES documents(document_id) ON DELETE CASCADE,
    to_document_id   BIGINT NOT NULL REFERENCES documents(document_id) ON DELETE CASCADE,
    relation         TEXT NOT NULL,   -- 'related' | 'companion' | 'precedent' | 'analyzes' | ...
    source           TEXT NOT NULL,   -- 'congress_gov' | 'editorial' | 'manual'
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (from_document_id, to_document_id, relation)
);
CREATE INDEX IF NOT EXISTS idx_document_relations_to ON document_relations (to_document_id);

-- Usage ledger for every metered AI call (token tracking per project rules).
CREATE TABLE IF NOT EXISTS ai_usage (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    called_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    provider      TEXT NOT NULL,             -- 'vertex_gemini' | 'ollama' | ...
    model         TEXT NOT NULL,
    purpose       TEXT,                      -- 'search_synthesis' | ...
    input_tokens  INTEGER,
    output_tokens INTEGER
);

-- ---------------------------------------------------------------------------
-- Experts / reviewers (see Services/ExpertService.cs). The vetted people who
-- peer-review studies and bill analyses. Seeded from the public roster
-- (api/seed_experts.py); "studies reviewed" and "bills reviewed" are derived
-- from the review join tables below, never stored as lists.
CREATE TABLE IF NOT EXISTS experts (
    expert_id    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    slug         TEXT UNIQUE NOT NULL,      -- 'ingrid-gould-ellen' (route id)
    full_name    TEXT NOT NULL,
    title        TEXT,                      -- current position
    affiliation  TEXT,
    category     TEXT,                      -- Academic | Think tank | Research center | Non-profit...
    focus        TEXT,                      -- one-line research focus
    bio          TEXT,
    credentials  TEXT,                      -- e.g. 'PhD, Economics (MIT)'
    linkedin_url TEXT,
    profile_url  TEXT,                      -- institutional profile
    scholar_url  TEXT,                      -- Google Scholar / ORCID
    website_url  TEXT,
    image_url    TEXT,
    email        TEXT,                      -- internal contact; never rendered publicly
    location     TEXT,
    conflicts    TEXT,                      -- standing conflict-of-interest disclosure
    notes        TEXT,                      -- internal admin notes; never rendered publicly
    active       BOOLEAN NOT NULL DEFAULT TRUE,
    joined_at    DATE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- A peer review of a study by an expert.
CREATE TABLE IF NOT EXISTS study_reviews (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    study_ref      TEXT NOT NULL REFERENCES studies(ref) ON DELETE CASCADE,
    expert_id      BIGINT NOT NULL REFERENCES experts(expert_id) ON DELETE CASCADE,
    recommendation TEXT,                    -- accept | minor_revisions | major_revisions | reject
    score          NUMERIC(3,1),            -- 0-10
    review_text    TEXT,
    reviewed_at    DATE,
    published      BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (study_ref, expert_id)
);

CREATE INDEX IF NOT EXISTS idx_study_reviews_expert ON study_reviews (expert_id);

-- The Center's authored bill reviews (four-stage analysis rendered at
-- /bills/{review_id}); JSONB shaped by prototype/data/bill-review.schema.json.
-- Live legislative status is merged in from `bills` at read time.
CREATE TABLE IF NOT EXISTS bill_reviews (
    review_id  TEXT PRIMARY KEY,          -- front-end route id, e.g. 'hr6644-119'
    bill_id    TEXT UNIQUE REFERENCES bills(bill_id) ON DELETE CASCADE,
    review     JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- A peer review of a bill analysis by an expert (attached to the authored
-- bill-review document above).
CREATE TABLE IF NOT EXISTS expert_bill_reviews (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    review_id      TEXT NOT NULL REFERENCES bill_reviews(review_id) ON DELETE CASCADE,
    expert_id      BIGINT NOT NULL REFERENCES experts(expert_id) ON DELETE CASCADE,
    recommendation TEXT,                    -- endorse | minor_revisions | major_revisions | reject
    score          NUMERIC(3,1),
    review_text    TEXT,
    reviewed_at    DATE,
    published      BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (review_id, expert_id)
);
CREATE INDEX IF NOT EXISTS idx_expert_bill_reviews_expert ON expert_bill_reviews (expert_id);
