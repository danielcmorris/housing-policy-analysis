"""
Configuration for the Minneapolis 2040 proof slice.

This is the "policy_events" + "geographies" + "sources" registry for ONE causal
analog: HR 6644 section 103 (infill / permit streamlining) evaluated against the
Minneapolis 2040 upzoning (single-family-only zoning ended 2020-01-01).

Everything here is data-driven config so the pipeline stays declarative. When this
graduates into the real datalake, these dicts become rows in Postgres tables.
"""

# ---------------------------------------------------------------------------
# The lever under study (one provision of the omnibus, NOT the whole bill)
# ---------------------------------------------------------------------------
LEVER = {
    "lever_id": "HR6644-L01",
    "bill": "HR 6644 (119th)",
    "section": "Sec. 103",
    "mechanism_type": "upzoning_permit_streamlining",
    "description": "Exemption on construction or modification of residential "
                   "housing located on an infill site.",
}

# ---------------------------------------------------------------------------
# The natural experiment we transfer from
# ---------------------------------------------------------------------------
POLICY_EVENT = {
    "event_id": "E-MPLS2040",
    "policy": "Minneapolis 2040 Comprehensive Plan (ended single-family-only zoning)",
    "treated_geo": "CBSA:33460",
    "effective_date": "2020-01-01",   # citywide single-family zoning eliminated
    "adopted_date": "2019-10-25",
}

# Study window. Symmetric 5 pre / 5 post around the 2020-01-01 treatment.
PRE_YEARS = list(range(2015, 2020))    # 2015-2019
POST_YEARS = list(range(2020, 2025))   # 2020-2024  (2025+ partial excluded)

# ---------------------------------------------------------------------------
# Geographies: treated + a control pool of comparable Midwest metros that did
# NOT adopt citywide upzoning in the window.
#   NOTE: control validity is a genuine assumption, not a fact. These are a
#   reasonable first pool; a production run must vet each for its own reforms.
# ---------------------------------------------------------------------------
TREATED = {"geo_id": "CBSA:33460", "cbsa": "33460",
           "name": "Minneapolis-St. Paul-Bloomington, MN-WI",
           "fred_permits": "MINN427BPPRIV"}

CONTROLS = [
    {"geo_id": "CBSA:41180", "cbsa": "41180", "name": "St. Louis, MO-IL",
     "fred_permits": "STLBPPRIV"},
    {"geo_id": "CBSA:28140", "cbsa": "28140", "name": "Kansas City, MO-KS",
     "fred_permits": "KANS129BPPRIV"},
    {"geo_id": "CBSA:18140", "cbsa": "18140", "name": "Columbus, OH",
     "fred_permits": "COLU139BPPRIV"},
    {"geo_id": "CBSA:26900", "cbsa": "26900", "name": "Indianapolis-Carmel-Anderson, IN",
     "fred_permits": "INDI918BPPRIV"},
]

ALL_METROS = [TREATED] + CONTROLS

# ---------------------------------------------------------------------------
# Sources (feeds the citation layer — every observation row points at one)
# ---------------------------------------------------------------------------
SOURCES = {
    "fhfa_hpi": {
        "name": "FHFA All-Transactions House Price Index (Metropolitan, NSA)",
        "url": "https://www.fhfa.gov/hpi/download/quarterly_datasets/hpi_at_metro.csv",
        "publisher": "U.S. Federal Housing Finance Agency",
    },
    "fred_bps": {
        "name": "New Private Housing Units Authorized by Building Permits (MSA), "
                "sourced by FRED from U.S. Census Bureau Building Permits Survey",
        "url": "https://fred.stlouisfed.org/series/{series}",
        "publisher": "U.S. Census Bureau via FRED (St. Louis Fed)",
    },
}

FHFA_URL = SOURCES["fhfa_hpi"]["url"]
FRED_CSV = "https://fred.stlouisfed.org/graph/fredgraph.csv?id={series}"

DB_PATH = "data/proof.db"
FHFA_CACHE = "data/hpi_at_metro.csv"
