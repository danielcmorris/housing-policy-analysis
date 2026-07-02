# Law Retrieval API

FastAPI + Postgres service that pulls an individual federal law from
[api.congress.gov](https://api.congress.gov) — metadata **and full text body** —
persists it, and serves it back. This is step 1 of the Housing Policy Analysis
Website (see `../CLAUDE.md`); front-end phases will add endpoints to this same app.

## Layout

| File | Role |
|------|------|
| `schema.sql` | Postgres DDL (source of truth): `sources`, `bills`, `bill_text_versions`, `raw_payloads` |
| `app/config.py` | Endpoint registry + secrets resolution (env → `creds/api.env`) |
| `app/db.py` | Async psycopg3 pool + `init_schema()` |
| `app/congress_client.py` | httpx client: fetch bill / text / body, retry+backoff, api_key redaction, raw-zone cache |
| `app/repository.py` | Normalize congress.gov JSON → schema, upsert, read back |
| `app/routers/bills.py` | `GET /bills/{congress}/{bill_type}/{bill_number}` |
| `app/main.py` | App wiring, lifespan (opens pool + applies schema), `/health` |

Design follows `analysis/minneapolis_proof/`: a `schema.sql` source-of-truth,
provenance-carrying rows (every bill points at the `congress_gov` source and
carries a `data_vintage` stamp), and a raw-zone cache of immutable upstream
responses (`api/data/`, gitignored, plus a `raw_payloads` JSONB mirror).

## Credentials

Secrets are read from the process environment first, then `creds/api.env`
(gitignored, the `@creds` location in `CLAUDE.md`). Expected keys:

```
# creds/api.env
DATABASE_URL=postgresql://housing:housing@localhost:5432/housing
CONGRESS_API_KEY=<your api.congress.gov key>
```

Get a key at <https://api.congress.gov/sign-up/>. The key is passed to
congress.gov as the `api_key` query param and is **never** logged, stored, or
written into cached files or provenance URLs (see `redact()`).

## Run

```bash
pip install -r api/requirements.txt

# local Postgres (matches the DATABASE_URL default)
docker compose -f api/docker-compose.yml up -d postgres

# start the API (applies schema.sql on boot)
bash api/run.sh          # -> http://localhost:8000
```

Then:

```bash
curl -s localhost:8000/health
curl -s localhost:8000/bills/119/hr/6644 | jq '{title, versions: (.text_versions|length)}'
# force a re-pull from congress.gov (bypasses DB + disk cache):
curl -s "localhost:8000/bills/119/hr/6644?refresh=true" | jq .title
```

On a cache/DB hit the endpoint serves stored data without calling congress.gov.

## Endpoint

`GET /bills/{congress}/{bill_type}/{bill_number}`

- `bill_type` ∈ `hr, s, hjres, sjres, hconres, sconres, hres, sres`
- `?refresh=true` re-fetches upstream and re-stores.
- Errors: `400` bad bill type, `404` unknown bill, `429` upstream rate limit,
  `502` other upstream failure.

## Tests

```bash
docker compose -f api/docker-compose.yml up -d postgres
python -m pytest api/tests -v
```

Parse/client tests run fully offline (httpx `MockTransport`, no network). The
end-to-end test drives the app with fixtures + the shipped
`documents/HR6644-119-enrolled.txt` as the text body; it is skipped
automatically if Postgres is unreachable.
