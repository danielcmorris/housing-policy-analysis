#!/usr/bin/env bash
# One-shot: raw fetch -> load observations -> DiD -> compose answer.
set -euo pipefail
cd "$(dirname "$0")"
bash download.sh
python3 fetch_load.py
python3 analyze_did.py
python3 report.py
echo
echo "Done. See ANSWER.md and data/results.json"
