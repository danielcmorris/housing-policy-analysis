"""
Stage 7 of the walkthrough: compose the answer.

In production this text is written by Claude/Gemini, but it is fed ONLY the
computed results.json (numbers + validity flags) and the source registry. The
model narrates; it does not compute and it must honor the parallel_trends_ok
flag. This script produces the same answer deterministically so the proof has a
concrete, inspectable output and so we can diff the model's version against the
ground-truth numbers.
"""
import json
import os

import config as C


def cite(source_id, series=None):
    s = C.SOURCES[source_id]
    url = s["url"].format(series=series) if series else s["url"]
    return f"{s['publisher']} — {s['name']} <{url}>"


def verdict_line(r):
    d = r["did_pct"]
    direction = "lower" if d < 0 else "higher"
    if r["parallel_trends_ok"]:
        return (f"**{abs(d):.1f} percentage points {direction}** than comparable "
                f"metros (difference-in-differences, parallel pre-trends hold — "
                f"slope gap {r['pre_trend_slope_gap']}/yr).")
    return (f"estimate withheld — the parallel-trends assumption FAILS "
            f"(pre-2020 slope gap {r['pre_trend_slope_gap']}/yr between treated and "
            f"controls), so a difference-in-differences number here would not be "
            f"credible. Raw DiD was {d:+.1f}pp but should not be attributed to the "
            f"policy without a matched synthetic control.")


def main():
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    with open("data/results.json") as f:
        R = json.load(f)

    price = R["results"]["price_index"]
    permits = R["results"]["permits_total"]
    pre = f"{R['pre_years'][0]}-{R['pre_years'][1]}"
    post = f"{R['post_years'][0]}-{R['post_years'][1]}"
    controls = ", ".join(c["name"] for c in price["controls"])

    md = f"""# HR 6644 §103 — estimated effect on housing prices & construction

**Question:** How will HR 6644 affect housing prices and construction?
**Provision analyzed:** {C.LEVER['section']} — {C.LEVER['description']}
**Mechanism:** `{C.LEVER['mechanism_type']}`

## Method (one lever, one analog)

This provision — exempting infill residential from certain permitting review — is
a supply-side upzoning/streamlining measure. The closest measurable natural
experiment is the **{C.POLICY_EVENT['policy']}**, whose citywide elimination of
single-family-only zoning took effect **{C.POLICY_EVENT['effective_date']}**.

Difference-in-differences compares the treated metro against a pool of comparable
Midwest metros that did **not** upzone, over {pre} (pre) vs {post} (post). Each
metro is indexed to its own {pre} mean = 100; the treated change minus the
control-average change is the effect plausibly attributable to the policy, net of
the national trend (interest rates, cycle) that hit every metro.

- **Treated:** {price['treated']['name']}
- **Controls:** {controls}

## Estimated effects

| Outcome | Minneapolis change | Control-avg change | Attributable (DiD) | Credible? |
|---|---|---|---|---|
| House prices | {price['treated']['effect_pct']:+.1f}% | {price['control_avg_effect_pct']:+.1f}% | **{price['did_pct']:+.1f} pp** | {'✅ yes' if price['parallel_trends_ok'] else '⚠️ no'} |
| Construction (permits) | {permits['treated']['effect_pct']:+.1f}% | {permits['control_avg_effect_pct']:+.1f}% | {permits['did_pct']:+.1f} pp | {'✅ yes' if permits['parallel_trends_ok'] else '⚠️ no'} |

## What this says about HR 6644 §103

- **House prices:** After upzoning, Minneapolis house prices grew {verdict_line(price)}
  Directionally consistent with the policy goal: more permitted supply → softer
  price growth. This is the credible result of the two.
- **Construction (permits):** {verdict_line(permits)}

**Net read for §103:** the evidence points toward **modest downward pressure on
price growth**, concentrated where supply can actually respond. The construction
effect is *unresolved on this data* and needs a matched control before any number
is quoted — the system flags that rather than inventing certainty.

## Assumptions & limitations (this is a proof slice, not a verdict)

- **One analog, one metric family.** A real answer pools several upzoning
  experiments (Oregon HB 2001, California SB 9) and reconciles against published
  evaluations (e.g. Pew's Minneapolis study).
- **Control validity is assumed, not vetted.** The control metros may have run
  their own reforms; each must be checked. Their wide permit dispersion
  ({permits['controls'][0]['effect_pct']:+.0f}% to
  {max(c['effect_pct'] for c in permits['controls']):+.0f}%) is why the permit
  pre-trends fail.
- **Simple-average control**, not a weighted synthetic control. Weighting the
  controls to match Minneapolis's pre-trend is the direct fix for the failed
  permit parallel-trends check.
- **Metro, not city.** The policy is citywide; metro aggregation dilutes it, so
  true effects are likely larger than measured here.
- **No local supply-elasticity transfer** to HR 6644's national target areas yet.

## Sources (every number above traces to one of these)

- {cite('fhfa_hpi')}
- {cite('fred_bps', series=C.TREATED['fred_permits'])} (and matching series per control metro)

*Numbers computed by `analyze_did.py` from the `observations` table; this narrative
only reports them. Intended to land in front of a professional reviewer, who judges.*
"""
    with open("ANSWER.md", "w") as f:
        f.write(md)
    print(md)


if __name__ == "__main__":
    main()
