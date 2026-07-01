"""
Stage 4 of the walkthrough: difference-in-differences.

Method (indexed DiD, transparent by hand — no numpy):
  1. Pull each metro's annual series for a metric from `observations`.
  2. Normalize each metro to its OWN pre-period (2015-19) mean = 100, so metros
     of different size are comparable and we measure % change vs baseline.
  3. Pre-trend check: OLS slope of the normalized series over 2015-19 for the
     treated metro vs. the control average. Similar slopes => parallel-trends
     assumption is credible (this is what makes DiD valid; we report it, not
     assume it).
  4. Effect = (post-period mean of normalized values) - 100, i.e. % change vs
     baseline. Treated effect minus control-average effect = the DiD estimate:
     the change plausibly attributable to the policy, net of the common trend
     every metro experienced (rates, national cycle).

All arithmetic happens HERE in code, never in the LLM.
"""
import json
import os
import sqlite3

import config as C


def series(con, geo, metric):
    rows = con.execute(
        "SELECT period, value FROM observations WHERE geo_id=? AND metric_code=? "
        "ORDER BY period", (geo, metric)).fetchall()
    return {int(y): float(v) for y, v in rows}


def ols_slope(pairs):
    """Least-squares slope of y over x for [(x,y),...]."""
    n = len(pairs)
    mx = sum(x for x, _ in pairs) / n
    my = sum(y for _, y in pairs) / n
    num = sum((x - mx) * (y - my) for x, y in pairs)
    den = sum((x - mx) ** 2 for x, _ in pairs)
    return num / den if den else 0.0


def normalize(s):
    """Index the series to its own pre-period mean = 100."""
    base = sum(s[y] for y in C.PRE_YEARS) / len(C.PRE_YEARS)
    return {y: v / base * 100.0 for y, v in s.items()}, base


def analyze_metric(con, metric):
    def metro_stats(geo):
        norm, base = normalize(series(con, geo, metric))
        post_mean = sum(norm[y] for y in C.POST_YEARS) / len(C.POST_YEARS)
        pre_slope = ols_slope([(y, norm[y]) for y in C.PRE_YEARS])
        return {"norm": norm, "baseline": round(base, 2),
                "post_mean_norm": round(post_mean, 2),
                "effect_pct": round(post_mean - 100.0, 2),
                "pre_trend_slope": round(pre_slope, 3)}

    treated = metro_stats(C.TREATED["geo_id"])
    treated["geo_id"] = C.TREATED["geo_id"]
    treated["name"] = C.TREATED["name"]

    controls = []
    for m in C.CONTROLS:
        st = metro_stats(m["geo_id"])
        st["geo_id"] = m["geo_id"]
        st["name"] = m["name"]
        controls.append(st)

    ctrl_effect = sum(c["effect_pct"] for c in controls) / len(controls)
    ctrl_slope = sum(c["pre_trend_slope"] for c in controls) / len(controls)
    did = treated["effect_pct"] - ctrl_effect
    slope_gap = abs(treated["pre_trend_slope"] - ctrl_slope)

    return {
        "metric": metric,
        "treated": treated,
        "controls": controls,
        "control_avg_effect_pct": round(ctrl_effect, 2),
        "control_avg_pre_trend_slope": round(ctrl_slope, 3),
        "did_pct": round(did, 2),
        "pre_trend_slope_gap": round(slope_gap, 3),
        # heuristic: normalized-index slopes within ~3 pts/yr of each other
        "parallel_trends_ok": slope_gap <= 3.0,
    }


def main():
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    con = sqlite3.connect(C.DB_PATH)
    out = {
        "lever": C.LEVER,
        "policy_event": C.POLICY_EVENT,
        "pre_years": [C.PRE_YEARS[0], C.PRE_YEARS[-1]],
        "post_years": [C.POST_YEARS[0], C.POST_YEARS[-1]],
        "results": {m: analyze_metric(con, m)
                    for m in ("permits_total", "price_index")},
    }
    with open("data/results.json", "w") as f:
        json.dump(out, f, indent=2)

    # readable summary
    for metric, r in out["results"].items():
        t = r["treated"]
        print(f"\n=== {metric} ===")
        print(f"  treated  {t['name'][:28]:28}  effect={t['effect_pct']:+6.1f}%"
              f"  (pre-trend slope {t['pre_trend_slope']:+.2f}/yr)")
        for c in r["controls"]:
            print(f"  control  {c['name'][:28]:28}  effect={c['effect_pct']:+6.1f}%")
        print(f"  control avg effect            {r['control_avg_effect_pct']:+6.1f}%"
              f"  (pre-trend slope {r['control_avg_pre_trend_slope']:+.2f}/yr)")
        print(f"  >> DiD (attributable)         {r['did_pct']:+6.1f} pp"
              f"   | parallel-trends ok: {r['parallel_trends_ok']} "
              f"(slope gap {r['pre_trend_slope_gap']})")
    print("\nwrote data/results.json")
    con.close()


if __name__ == "__main__":
    main()
