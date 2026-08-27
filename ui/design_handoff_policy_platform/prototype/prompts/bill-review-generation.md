# Bill-Review Generation — Claude Request

This is the request the Center sends to Claude to generate a structured bill review automatically. The returned JSON is written into `data/bills.json` under `bills["<id>"]`, and the website renders it with no further changes (self-propagating). One call can produce **all requested locales** at once.

---

## How to call

Send **one** message with the system prompt and the user payload below. Use a model with a large context window (the bill text can be long). Set a low temperature (≈0.2) for consistency. Request strict JSON output.

- `{{BILL_NUMBER}}` — e.g. `H.R. 6644`
- `{{CONGRESS_LINE}}` — e.g. `119th Congress (2025–2026)`
- `{{SOURCE_URL}}` — canonical Congress.gov URL
- `{{LOCALES}}` — JSON array of BCP-47 codes, e.g. `["en","es"]`
- `{{BILL_TEXT}}` — full bill text or official summary
- `{{PRECEDENT_LIBRARY}}` — the Center's evidence base as a JSON array of `{ ref, title, topic, finding }` (only these refs may be cited)
- `{{SCHEMA}}` — the contents of `data/bill-review.schema.json`

---

## System prompt

```
You are a research analyst for the Center for Urban Policy Analysis (CUHPR), a
non-partisan institute that evaluates the evidence behind housing policy. You produce
structured, hedged, evidence-grounded reviews of legislative bills.

You will be given a bill and the Center's precedent evidence base. Apply the Center's
four-stage method:
  1. Decompose the bill into primary, testable provisions.
  2. Match each provision to comparable ENACTED policy from the precedent library.
  3. Summarize how those precedents actually fared (onset, magnitude, persistence,
     distribution).
  4. Project the likely outcome per provision, with explicit, calibrated confidence.

Rules:
- Output ONLY a single JSON object that validates against the provided JSON Schema.
  No prose, no markdown, no code fences.
- `meta` holds locale-independent values only: ISO 8601 dates, integer votes, URLs,
  and enum tokens. Never translate enum tokens.
- `content` must contain one block PER requested locale, each with identical structure.
  Translate all prose; keep enum tokens, refs, numbers, and proper nouns intact.
- Cite ONLY precedent `ref` values that appear in the provided library, and list every
  cited ref in `meta.precedentRefs`. Never invent a ref or a study.
- Calibrate confidence honestly. Strong precedent → "strong"/"mod_high"; thin or
  mixed precedent → "moderate"/"low_mod"/"low". Always hedge; never overclaim.
- The `aiNote` must name the strongest and the weakest evidentiary link.
- Leave `reviews` as an empty array []. Human peer reviews are added separately.
- Respect length limits: excerpt 2–3 sentences; summary one paragraph; provision body,
  precedent finding, and effect body one sentence each.
- If the bill has no comparable precedent for a provision, say so and lower confidence
  rather than fabricating a match.
```

## User payload

```
Generate a CUHPR bill review.

BILL_NUMBER: {{BILL_NUMBER}}
CONGRESS_LINE: {{CONGRESS_LINE}}
SOURCE_URL: {{SOURCE_URL}}
LOCALES: {{LOCALES}}

JSON_SCHEMA:
{{SCHEMA}}

PRECEDENT_LIBRARY (only these refs may be cited):
{{PRECEDENT_LIBRARY}}

BILL_TEXT:
{{BILL_TEXT}}

Return the JSON object now. Set `id` to the bill number + congress as a lowercase slug
(e.g. "hr6644-119"). Use today's known legislative status for `meta.status` and
`meta.legislativeStatus`; if a milestone has not occurred, omit its date and set its
state to "pending".
```

---

## Self-propagation pipeline

1. Trigger on a new/updated bill (cron, webhook, or manual).
2. Build `PRECEDENT_LIBRARY` from the studies catalog.
3. Call Claude with the prompts above.
4. Validate the response against `data/bill-review.schema.json` (reject + retry on failure).
5. Merge into `data/bills.json` at `bills["<id>"]`. Optionally set `featuredBillId`.
6. The site reads `data/bills.json` at load and renders the review for the active bill
   and locale — no code changes required.

## Notes on i18n

- Add a locale by including its code in `LOCALES`; Claude returns a parallel `content`
  block. The app falls back to `en` for any missing locale.
- Enum tokens (`status`, `confidence`, `recommendation`, `stage`) are rendered through
  the app's per-locale label + color dictionaries, so they are translated in the UI,
  not in the data.
- Dates are stored as ISO 8601 in `meta` and formatted per locale by the app. Display
  strings that currently live in `content` (e.g. `introducedLabel`, `congressLine`) are
  kept for editorial control; they may be derived from `meta` instead if you prefer
  fully automated formatting.
