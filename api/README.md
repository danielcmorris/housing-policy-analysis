# Python maintenance scripts (no HTTP API here)

The HTTP API for this project is **HousingPolicy.Api** (C# / ASP.NET Core) —
per project convention, anything exposing an endpoint is C#. This folder holds
the Python *scripts* that maintain the database, plus the shared library they
use (`app/`):

| Script | Purpose |
|--------|---------|
| `python -m api.sync_legislation --seed 119/hr/6644 --window-days 7 --texts` | Discover/refresh housing bills from congress.gov and pull Formatted-Text bodies (cron-friendly; hard caps in `app/config.py`) |
| `python -m api.seed_reviews [store.json]` | Load authored bill-review documents (bill-review.schema.json shape) into `bill_reviews` |

Run from the repo root with the venv active; secrets resolve from the process
environment first, then `creds/api.env` (DATABASE_URL, CONGRESS_API_KEY).

`schema.sql` here matches HousingPolicy.Api/schema.sql for the tables these
scripts touch; the C# service applies its schema on boot and is the source of
truth for the serving layer.
