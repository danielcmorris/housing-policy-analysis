# Minneapolis proof slice — HR 6644 §103

A **vertical slice** of the housing-policy analysis architecture: take one
provision of one bill, find a real natural-experiment analog, estimate the effect
on prices and construction with real public data, and compose a cited answer that
honors its own statistical validity checks.

It proves the whole chain end-to-end on one lever before scaling to the full bill
and corpus.

## What it does (maps to the design walkthrough)

| Stage | File | Output |
|-------|------|--------|
| Decompose bill → lever | `config.py` (`LEVER`) | HR 6644 §103, mechanism `upzoning_permit_streamlining` |
| Match causal analog | `config.py` (`POLICY_EVENT`, controls) | Minneapolis 2040 vs 4 control metros |
| Assemble treated/control panel | `download.sh` + `fetch_load.py` | long-format `observations` table (SQLite) |
| Difference-in-differences + pre-trend check | `analyze_did.py` | `data/results.json` |
| Compose cited answer | `report.py` | `ANSWER.md` |

## Run it

```bash
./run.sh          # fetch -> load -> analyze -> report
```

Pure Python 3 stdlib — no pip installs, no API keys, **no Google services**.

## Data sources (keyless, public)

- **Prices:** FHFA All-Transactions House Price Index, metro level (NSA).
- **Construction:** New Private Housing Units Authorized by Building Permits, per
  MSA, via FRED (sourced from the U.S. Census Bureau Building Permits Survey).

`download.sh` caches the raw files in `data/` (the immutable "raw zone");
`fetch_load.py` parses them into the `observations` serving table defined in
`schema.sql`. That table is written Postgres-first so it ports to the real
pgvector datalake unchanged.

## The key result (and why it matters)

- **Prices:** Minneapolis grew **−11.3 pp** vs comparable metros after upzoning,
  and the parallel-trends assumption **holds** → reported as credible.
- **Permits:** parallel-trends **fails** (Minneapolis was already on a steep
  pre-2020 permit uptrend) → the estimate is **withheld**, not fabricated.

That second outcome is the point: the system refuses to attribute an effect when
the identifying assumption breaks, instead of emitting a confident wrong number.

## Honest limitations

See the "Assumptions & limitations" section auto-generated into `ANSWER.md`.
Headline upgrades for a production run:
1. **Weighted synthetic control** (match the pre-trend) — fixes the permit check.
2. **Pool multiple analogs** (Oregon HB 2001, California SB 9) + reconcile with
   published evaluations in the `studies` corpus.
3. **Vet each control** for its own reforms; add per-capita normalization and a
   local supply-elasticity transfer to HR 6644's target geographies.
