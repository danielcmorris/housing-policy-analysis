# HousingPolicy.Api — Law Retrieval API (C#)

ASP.NET Core Web API + Postgres service that pulls an individual federal law
from [api.congress.gov](https://api.congress.gov) — metadata **and the most
recent raw text** (the latest legislative stage, e.g. Enrolled) — persists it,
and serves it back. This is the C# port of the original
`api/` FastAPI prototype, rebuilt in the house stack (Controllers / Services /
Modules, **Dapper + Npgsql, raw SQL**, no ORM) to match the other Morris Dev
Web APIs. Step 1 of the Housing Policy Analysis Website (see `../CLAUDE.md`);
the Angular front end and later endpoints will grow on this same app.

## Layout

| Path | Role |
|------|------|
| `schema.sql` | Postgres DDL (source of truth): `sources`, `bills`, `bill_text_versions`, sub-resource tables (`bill_sponsors`, `bill_cosponsors`, `bill_amendments`, `bill_actions`, `bill_committees`, `bill_subjects`, `bill_summaries`, `bill_titles`, `bill_related_bills`), `raw_payloads`. Copied to output; applied on startup. |
| `Program.cs` | Wiring: Dapper underscore mapping + DateOnly handlers, config layering (appsettings → `creds/config.json` → env), DI, CORS, Swagger, schema init on boot. |
| `Options/CongressOptions.cs` | `Congress` config section (BaseUrl, ApiKey, DataDir, timeout, retries). |
| `Modules/DataLayerBase.cs` | Npgsql + Dapper access base (house pattern from DCElectricWebAPI). |
| `Services/CongressClient.cs` | Typed `HttpClient`: fetch bill / text / body, retry+backoff (honors Retry-After), api_key redaction, raw-zone disk cache, typed exceptions. |
| `Services/BillRepository.cs` | Normalize congress.gov JSON → schema, select the most recent text version, upsert in a transaction, read back. |
| `Services/SchemaInitializer.cs` | Applies `schema.sql` + ensures the `congress_gov` source row on startup. |
| `Json/DateOnlyHandler.cs` | Dapper ↔ Npgsql `date` handlers (house pattern from pfsa-api). |
| `Models/Bill.cs`, `Models/TextVersion.cs` | DB projection + JSON response DTOs. |
| `Controllers/BillsController.cs` | `GET /api/bills/{congress}/{billType}/{billNumber}` (+ `?refresh=true`). |
| `Controllers/HealthController.cs` | `GET /health` (DB ping). |

Design mirrors the corpus: a `schema.sql` source-of-truth, provenance rows
(every bill points at `congress_gov` and carries a `data_vintage` stamp), and a
raw-zone cache of immutable upstream responses (`Data/`, gitignored, plus a
`raw_payloads` JSONB mirror).

## Credentials / config

Config is layered (highest priority last): `appsettings.json` →
`creds/config.json` (repo root, gitignored — the `@creds` location in
`CLAUDE.md`) → environment variables. Expected shape of `creds/config.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Port=5432;Database=policydb;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true"
  },
  "Congress": { "ApiKey": "<your api.congress.gov key>" }
}
```

Get a key at <https://api.congress.gov/sign-up/>. It is passed to congress.gov
as the `api_key` query param and is **never** logged, stored, or written into
cached files or provenance URLs (see `CongressClient.Redact`).

## Run

```bash
cd HousingPolicy.Api
dotnet run                                   # -> http://localhost:5xxx (see console)
# or pin a port:
dotnet run --urls http://localhost:8138
```

Schema is applied on boot. Then:

```bash
curl -s localhost:8138/health
curl -s localhost:8138/api/bills/119/hr/6644 | jq '{title, versions:(.textVersions|length)}'
# force a re-pull from congress.gov (bypasses DB + disk cache):
curl -s "localhost:8138/api/bills/119/hr/6644?refresh=true" | jq .title
```

On a DB/cache hit the endpoint serves stored data without calling congress.gov.

## Endpoint

`GET /api/bills/{congress}/{billType}/{billNumber}`

- `billType` ∈ `hr, s, hjres, sjres, hconres, sconres, hres, sres`
- `?refresh=true` re-fetches upstream and re-stores.
- Errors: `400` bad bill type, `404` unknown bill, `429` upstream rate limit,
  `502` other upstream failure.

## What gets stored

- **Bill metadata** → `bills` (title, chamber, sponsor, `introducedDate`,
  `policyArea`, latest action, `updateDate`, provenance).
- **One text version** → `bill_text_versions`: only the *most recent* raw text,
  meaning the latest legislative stage. congress.gov lists text versions
  newest-first, so the client takes the first version that offers a "Formatted
  Text" format (Enrolled for a passed bill, otherwise the newest stage). Older
  stages (introduced, engrossed, …) are not downloaded or stored.
- **Sub-resource metadata**, each from its own endpoint (~1 small paged call
  per import) → its own table, replaced wholesale on refresh:
  `bill_sponsors`, `bill_cosponsors`, `bill_amendments`, `bill_actions`,
  `bill_committees` (activities as JSONB), `bill_subjects`, `bill_summaries`
  (CRS summary HTML), `bill_titles`, `bill_related_bills`.
- **Raw upstream JSON** → `raw_payloads` (the full `/bill` and `/text`
  responses, JSONB) for audit / re-parse.

The endpoint returns all of the above nested under the `Bill` JSON.

## Notes

- **Upstream throttling:** congress.gov sits behind Cloudflare and can
  throttle/hold connections after a burst of rapid requests. Because each import
  now fetches only a single text body, this is largely a non-issue for one-off
  imports; a future bulk/backfill job should still space out its calls.
- The client retries timeouts/429/5xx up to 3× with backoff (honoring
  Retry-After). Persistent stalls usually mean you are being rate-shaped.
