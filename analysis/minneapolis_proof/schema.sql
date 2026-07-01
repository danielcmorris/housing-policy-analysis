-- Serving-layer schema for the housing-policy datalake (proof slice).
-- Written Postgres-first (this is the eventual target); the proof pipeline
-- creates the identical shape in SQLite so it ports over unchanged.
--
-- Design principle: long/tidy fact table keyed by (geo, metric, period).
-- One row = one measured value, and every row carries its own provenance,
-- so any query result is self-citing.

CREATE TABLE IF NOT EXISTS sources (
    source_id   TEXT PRIMARY KEY,   -- 'fhfa_hpi', 'fred_bps'
    name        TEXT NOT NULL,
    publisher   TEXT,
    url         TEXT
);

CREATE TABLE IF NOT EXISTS geographies (
    geo_id      TEXT PRIMARY KEY,   -- 'CBSA:33460'
    geo_type    TEXT,               -- 'metro'
    cbsa        TEXT,               -- '33460'  (FIPS/CBSA anchor)
    name        TEXT
);

CREATE TABLE IF NOT EXISTS metrics (
    metric_code TEXT PRIMARY KEY,   -- 'permits_total', 'price_index'
    unit        TEXT,
    cadence     TEXT,               -- native cadence before we roll up to annual
    description TEXT
);

-- THE fact table.
CREATE TABLE IF NOT EXISTS observations (
    geo_id       TEXT NOT NULL REFERENCES geographies(geo_id),
    metric_code  TEXT NOT NULL REFERENCES metrics(metric_code),
    period       INTEGER NOT NULL,          -- year (annual grain)
    value        REAL,
    source_id    TEXT NOT NULL REFERENCES sources(source_id),
    data_vintage TEXT,                       -- retrieval stamp / release id
    PRIMARY KEY (geo_id, metric_code, period)
);

CREATE INDEX IF NOT EXISTS idx_obs_metric ON observations (metric_code, geo_id, period);
