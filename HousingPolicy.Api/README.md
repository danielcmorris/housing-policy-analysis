# HousingPolicy.Api — Law Retrieval API (C#)

ASP.NET Core Web API + Postgres service that pulls an individual federal law
from [api.congress.gov](https://api.congress.gov) — metadata **and full text
body** — persists it, and serves it back. This is the C# port of the original
`api/` FastAPI prototype, rebuilt in the house stack (Controllers / Services /
Modules, **Dapper + Npgsql, raw SQL**, no ORM) to match the other Morris Dev
Web APIs. Step 1 of the Housing Policy Analysis Website (see `../CLAUDE.md`);
the Angular front end and later endpoints will grow on this same app.

## Layout

| Path | Role |
|------|------|
| `schema.sql` | Postgres DDL (source of truth): `sources`, `bills`, `bill_text_versions`, `raw_payloads`. Copied to output; applied on startup. |
| `Program.cs` | Wiring: Dapper underscore mapping + DateOnly handlers, config layering (appsettings → `creds/config.json` → env), DI, CORS, Swagger, schema init on boot. |
| `Options/CongressOptions.cs` | `Congress` config section (BaseUrl, ApiKey, DataDir, timeout, retries). |
| `Modules/DataLayerBase.cs` | Npgsql + Dapper access base (house pattern from DCElectricWebAPI). |
| `Services/CongressClient.cs` | Typed `HttpClient`: fetch bill / text / body, retry+backoff (honors Retry-After), api_key redaction, raw-zone disk cache, typed exceptions. |
| `Services/BillRepository.cs` | Normalize congress.gov JSON → schema, upsert in a transaction, read back. |
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

## Notes

- **Upstream throttling:** congress.gov sits behind Cloudflare and will
  throttle/hold connections after a burst of rapid requests (a single bill's
  refresh fetches ~8 text bodies). The client retries timeouts/429/5xx up to 3×
  with backoff; if you see requests stalling, you are likely being rate-shaped —
  back off and retry. A future sync job should serialize/space these fetches.
