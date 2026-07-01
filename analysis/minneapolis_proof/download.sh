#!/usr/bin/env bash
# Raw-zone fetch: download the keyless public source files with curl into data/.
# Kept separate from parsing so the pipeline is re-runnable offline and the
# downloaded files are an immutable cache. No Google services; no API keys.
set -euo pipefail
cd "$(dirname "$0")/data"

FHFA="https://www.fhfa.gov/hpi/download/quarterly_datasets/hpi_at_metro.csv"
# FRED building-permit series (total private units, NSA) per metro
SERIES=(MINN427BPPRIV STLBPPRIV KANS129BPPRIV COLU139BPPRIV INDI918BPPRIV)

echo "FHFA metro HPI ..."
curl -sS -m 90 -L "$FHFA" -o hpi_at_metro.csv
echo "  $(wc -c < hpi_at_metro.csv) bytes"

for s in "${SERIES[@]}"; do
  echo "FRED $s ..."
  curl -sS -m 60 -L "https://fred.stlouisfed.org/graph/fredgraph.csv?id=${s}" -o "fred_${s}.csv"
  echo "  $(wc -l < fred_${s}.csv) rows"
done
echo "done."
