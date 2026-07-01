"""
Stage 1-3 of the walkthrough: pull real data and load the long-format
`observations` table.

  price_index  <- FHFA All-Transactions metro HPI (annual = mean of 4 quarters, NSA)
  permits_total <- FRED / Census BPS monthly permits (annual = sum of 12 months)

Pure stdlib (urllib, csv, sqlite3) so it runs on a bare Python 3 with no pip installs.
No Google services are called. All sources are keyless public U.S. gov / Fed data.
"""
import csv
import io
import os
import sqlite3
import urllib.request
from collections import defaultdict

import config as C

TIMEOUT = 60


def _get(url: str) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": "housing-policy-proof/0.1"})
    with urllib.request.urlopen(req, timeout=TIMEOUT) as r:
        return r.read().decode("utf-8", "replace")


def init_db(con: sqlite3.Connection) -> None:
    with open(os.path.join(os.path.dirname(__file__), "schema.sql")) as f:
        con.executescript(f.read())
    # dimensions
    for sid, s in C.SOURCES.items():
        con.execute("INSERT OR REPLACE INTO sources VALUES (?,?,?,?)",
                    (sid, s["name"], s["publisher"], s["url"]))
    for m in C.ALL_METROS:
        con.execute("INSERT OR REPLACE INTO geographies VALUES (?,?,?,?)",
                    (m["geo_id"], "metro", m["cbsa"], m["name"]))
    con.executemany("INSERT OR REPLACE INTO metrics VALUES (?,?,?,?)", [
        ("price_index", "index (2000=100, NSA)", "quarterly",
         "FHFA all-transactions house price index, annual mean of quarters"),
        ("permits_total", "housing units authorized", "monthly",
         "New private housing units authorized by building permits, annual sum"),
    ])
    con.commit()


def load_prices(con: sqlite3.Connection) -> int:
    """FHFA metro file: name,cbsa,year,quarter,index_nsa,(sa_change). No header row."""
    if not os.path.exists(C.FHFA_CACHE):
        with open(C.FHFA_CACHE, "w") as f:
            f.write(_get(C.FHFA_URL))
    wanted = {m["cbsa"]: m["geo_id"] for m in C.ALL_METROS}
    years = set(C.PRE_YEARS + C.POST_YEARS)
    # accumulate quarterly index values per (geo, year)
    acc = defaultdict(list)
    with open(C.FHFA_CACHE, newline="") as f:
        for row in csv.reader(f):
            if len(row) < 5:
                continue
            cbsa, yr, idx = row[1], row[2], row[4]
            if cbsa not in wanted or not yr.isdigit() or int(yr) not in years:
                continue
            try:
                acc[(wanted[cbsa], int(yr))].append(float(idx))
            except ValueError:
                continue  # '-' = missing quarter
    n = 0
    for (geo, yr), vals in acc.items():
        annual = sum(vals) / len(vals)  # mean of available quarters
        con.execute("INSERT OR REPLACE INTO observations VALUES (?,?,?,?,?,?)",
                    (geo, "price_index", yr, round(annual, 3), "fhfa_hpi", "hpi_at_metro"))
        n += 1
    con.commit()
    return n


def load_permits(con: sqlite3.Connection) -> int:
    """FRED monthly CSV per metro: observation_date,VALUE -> annual sum.

    Reads the local raw-zone file data/fred_<series>.csv produced by download.sh.
    Falls back to a live urllib fetch if the cache is missing.
    """
    years = set(C.PRE_YEARS + C.POST_YEARS)
    n = 0
    for m in C.ALL_METROS:
        series = m["fred_permits"]
        cache = os.path.join("data", f"fred_{series}.csv")
        if os.path.exists(cache):
            with open(cache) as f:
                text = f.read()
        else:
            text = _get(C.FRED_CSV.format(series=series))
        acc = defaultdict(list)
        rdr = csv.reader(io.StringIO(text))
        next(rdr, None)  # header
        for row in rdr:
            if len(row) < 2 or not row[1].strip():
                continue
            yr = int(row[0][:4])
            if yr in years:
                try:
                    acc[yr].append(float(row[1]))
                except ValueError:
                    continue
        for yr, vals in acc.items():
            if len(vals) == 12:  # only keep full years (true annual total)
                con.execute("INSERT OR REPLACE INTO observations VALUES (?,?,?,?,?,?)",
                            (m["geo_id"], "permits_total", yr, sum(vals),
                             "fred_bps", series))
                n += 1
    con.commit()
    return n


def main() -> None:
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    con = sqlite3.connect(C.DB_PATH)
    init_db(con)
    p = load_prices(con)
    q = load_permits(con)
    print(f"loaded observations: price rows={p}, permit rows={q}")
    # quick integrity read-back
    for metric in ("price_index", "permits_total"):
        rows = con.execute(
            "SELECT geo_id, COUNT(*), MIN(period), MAX(period) FROM observations "
            "WHERE metric_code=? GROUP BY geo_id ORDER BY geo_id", (metric,)).fetchall()
        print(f"\n{metric}:")
        for r in rows:
            print(f"  {r[0]:12} n={r[1]}  {r[2]}-{r[3]}")
    con.close()


if __name__ == "__main__":
    main()
