# Handoff: Center for Urban Policy Analysis — Research Platform

## Overview
A formal, non-partisan policy-research platform. It publishes rigorous reviews of housing/urban-economic **studies** and of pending **legislation**, backed by a searchable **Data Commons** (a cross-referenced corpus of federal/state/city policy + regional economic series) and an **AI research layer** that summarizes, critiques methodology, drafts peer-review questions, and answers questions against the corpus.

The signature product is the **Bill Review**: every bill is run through a four-stage method — (1) decompose into testable provisions, (2) match each to comparable enacted policy from the last ~50 years, (3) trace how those precedents actually fared, (4) project the likely outcome with explicit, calibrated confidence — then peer-reviewed.

## About the Design Files
The files in `prototype/` are a **design reference created in HTML** — a single-file interactive prototype (`Housing Policy Review.dc.html`) demonstrating intended look, layout, copy, and behavior. **It is not production code.** It runs on a proprietary preview runtime (a `support.js` that is intentionally not included) and is authored as one large declarative component with an inline-styled template + a plain-JS logic class.

Your task is to **recreate these designs in the target stack** using its established patterns — not to ship the HTML. Target stack (confirmed by the team):
- **Frontend:** Angular 20 + Angular Material 20 (matches the org's existing `Morrisdev/aiticket` app and the bound Dash Support design system). If you instead stand up a React/other SPA, keep the visual spec below exact.
- **Backend:** .NET 8 / ASP.NET Core Web API, C#.
- **Data:** PostgreSQL + **pgvector** for the cross-referenced Commons and semantic "Ask."
- **AI:** server-side calls to Claude (see `prototype/prompts/bill-review-generation.md`).

> Note on visual style: the prototype deliberately uses a **formal crimson/black scholarly** treatment (Spectral serif + Public Sans + Roboto Mono), which diverges from the blue "Dash Support" Material kit. Treat the crimson system below as the source of truth for THIS product; reuse Dash Support component structure/behavior where convenient but honor these tokens.

## Fidelity
**High-fidelity.** Colors, typography, spacing, states, and interactions are final. Recreate pixel-accurately. Exact tokens are in the Design Tokens section; the prototype is the visual reference of record.

---

## Architecture at a glance

```
Angular SPA ──HTTPS──> ASP.NET Core Web API ──> PostgreSQL + pgvector
                              │                         ├─ bills / reviews (JSONB per schema)
                              │                         ├─ commons_entries (+ embedding vector)
                              │                         ├─ studies, precedents, reviewers
                              │                         └─ member datasets (upload + review workflow)
                              ├──> Claude API (review generation, "Ask", question drafting)
                              ├──> Congress.gov ingestion (legislation tracker)
                              └──> Auth (PowerDash postMessage handshake, per aiticket)
```

The bill-review **data contract already exists**: `prototype/data/bill-review.schema.json` (JSON Schema draft 2020-12). Persist review content as JSONB validated against it; store locale-independent facts in typed columns. The generation prompt in `prototype/prompts/bill-review-generation.md` returns objects in exactly this shape.

---

## Screens / Views

The prototype is a single-page app with a persistent **masthead → utility bar → sticky primary nav → screen body → footer**. Nav routes: Home, Studies Library, AI Research Assistant, Data Commons, US Congress, Resources, About (+ detail routes: Study, Bill Review). A dark-mode toggle sits in the masthead (persisted to `localStorage`, key `cuhpr-theme`).

### 1. Home
- **Purpose:** Formal first impression; surface the featured bill and latest research.
- **Layout:** Full-width sections, each an inner column capped at **1240px**, `padding: 0 32px`.
  - **Hero:** two columns `1.35fr / 1fr` split by a 1px hairline. Left: eyebrow (uppercase, crimson), 50px serif H1, 18px intro, two buttons. Right: **Featured Bill** card (category, clickable serif title, one-paragraph summary, "119th Congress · H.R. 6644" meta, "Read our full review →" + Congress.gov links).
  - **Topic strip:** dark bar (`--inverse`) of research-program chips.
  - **Latest Research:** 3-col grid of study cards with hairline dividers.
  - **How it works:** three-stage process, centered.
  - **Stats band:** crimson full-bleed, 4 stats.
  - **Commentary + AI callout** two-column.
- **Key components:** Featured Bill card (title routes to Bill Review), primary/secondary buttons.

### 2. Studies Library
- **Purpose:** Browse/filter reviewed studies.
- **Layout:** `248px / 1fr` — sticky filter rail (Topic checkboxes with counts, Review-status legend) + results list.
- **Result row:** grid `1fr auto` — category eyebrow + mono ref, 22px serif title, excerpt, authors · year · status pill; right: a boxed **Clarity Score** (0–10, colored by band). Pagination row.
- **Copy/refs:** study refs formatted `CUHPR-YYYY-NNNN` (monospace).

### 3. Study Detail
- **Layout:** `1fr / 320px` article + sticky rail.
- **Article:** Abstract (italic, crimson left-border), Key Findings, Methodology, an **AI Methodology Note** callout (gold left-border, robot icon).
- **Rail:** Clarity Score card, Review Status timeline, Metadata table.
- **Actions:** Download PDF, "Ask the AI about this study", "Source Data" (→ Data Commons).

### 4. AI Research Assistant
- **Purpose:** Chat against the reviewed corpus.
- **Layout:** `1fr / 300px` — chat panel + capabilities rail.
- **Chat panel:** dark header (avatar + "Reading N reviewed studies"); scrolling message list; **user** bubbles right (dark fill), **AI** bubbles left (paper fill, crimson left-border) with a **Sources** block listing cited `CUHPR-…` refs (clickable). Typing indicator = three pulsing dots. Composer: suggested-prompt chips + auto-growing textarea + circular send button. Enter sends; Shift+Enter newline.
- **Behavior in prototype:** canned keyword-routed replies. **In production:** stream from Claude with retrieval over `commons_entries`/studies; return structured citations.

### 5. Data Commons
- **Purpose:** The searchable cross-referenced repository + member contribution.
- **Sections:**
  - **Hero** (dark) + **stats band** (crimson): records / jurisdictions / sources / ingestion cadence.
  - **Search & Ask** (primary interactive): one input doubles as **search** (live token-filter of results) and **Ask** (Enter or Ask button → a "Commons Answer" with a *Drawn from* citation list). Three suggestion chips. **Filter chips**: Level (All/Federal/State/City), Outcome (Passed & failed/Passed/Failed), Category (Supply-Side/Regulation Reduction/Rent Regulation/Affordability). Results list: grid `120px 1fr auto` — level+year, title+place+summary+category/tag chips, Passed/Failed status pill.
  - **What it is / Cross-referenced six ways / Contribute** (4-step member dataset submission with an upload card) / an honest-limitations note.
- **Production mapping:** results = `commons_entries` filtered by columns; "Ask" = embed the query (pgvector `<=>` nearest neighbors over `commons_entries.embedding`, optionally filtered by the active chips) → pass top-k to Claude for the synthesized answer + citations. Member upload = file to object storage + a `dataset_submissions` review workflow (states: submitted → provenance → methodological review → published).

### 6. US Congress (Federal Legislation Tracker)
- **Purpose:** Search pending legislation + tiles of active bills.
- **Layout:** dark hero with a large search input (live token-filter by bill #, title, sponsor, topic; clear "×"); then **Active Legislation** = 2-col tile grid.
- **Tile:** chamber icon (House `account_balance` / Senate `gavel`), mono bill # · congress, status pill (Senate Passed=green, In Committee=amber, Introduced=grey), 20px serif title, summary, footer = category chip + sponsor + updated date. Featured bill gets a 3px crimson top border and links to its Bill Review. Empty state + Congress.gov source note.
- **Production mapping:** `legislation` table synced from Congress.gov; status enum drives pill color; tiles link to a Bill Review when one is published, else a "tracked pending review" state.

### 7. Bill Review (detail) — the flagship
- **Purpose:** Full four-stage review of one bill; rendered entirely from data.
- **Layout:** header band + `1fr / 320px` (article + sticky rail).
- **Header:** category eyebrow, mono bill #, status pill, 38px serif title, congress · introduced · sponsor line, actions (View on Congress.gov, Ask the AI, Source Data).
- **Article stages:**
  - **Summary** paragraph + method-note (hairline left-border).
  - **01 Primary Provisions** — 2-col cards `{tag, title, body}`.
  - **02 Historical Precedent** — clickable rows `{ref, match, title, finding}` + an **Evidence** strength chip (Strong/Moderate/… colored).
  - **03 Realized Effects of Precedent** — labelled rows (Onset/Magnitude/Persistence/Distribution).
  - **04 Projected Outcome** — table: Provision / Expected Effect / Horizon / Confidence (color-coded).
  - **AI Analysis Note** (gold callout) → "Continue this analysis" routes to AI.
  - **Peer Reviews** — reviewer cards (avatar, name, affiliation, date, recommendation pill, score, text).
- **Rail:** Overall Outlook (headline + confidence), Legislative Status timeline (with vote tallies), Key Facts table.
- **Data:** one object per `prototype/data/bills.json` → `bills["<id>"]`, validated against the schema. The prototype fetches `data/bills.json` at load and falls back to an embedded copy; **production replaces this with `GET /api/bills/{id}`**.

### 8. Resources & 9. About
- **Resources:** curated external links (Congress.gov, GovInfo, law/academic sources) grouped into cards.
- **About:** dark mission hero, three principles (Non-partisan / Transparent / Rigorous), leadership grid.

---

## Interactions & Behavior
- **Routing:** client-side; each nav item swaps the screen body. Detail views (Study, Bill Review) are their own routes. `window.scrollTo(0,0)` on navigate. Use Angular Router in production; give slides/screens stable route paths.
- **Dark mode:** `data-theme="dark"` on the root swaps a full CSS-variable palette; toggle persists to `localStorage['cuhpr-theme']`. Honor `prefers-color-scheme` for first visit if you like.
- **Search (Commons & Congress):** live token filter — lowercase, split on non-alphanumerics, drop stopwords/short tokens, match if **any** token is a substring of a concatenated haystack (title+place+summary+category+tags+sponsor). Debounce input in production.
- **Ask (Commons) / Chat (AI):** prototype uses keyword-routed canned text; production = retrieval + Claude with streamed tokens and structured citations. Show a typing/loading indicator; render citations as links to the source study/bill.
- **Filter chips:** single-select per group (Level/Outcome/Category); compose with the search term.
- **Hover/press:** links tint to crimson (`--accent`); tiles/cards use pointer cursor; buttons keep 2px radius. Motion is subtle (150–200ms ease) — no bounces/parallax.
- **Empty states:** Congress search shows a `search_off` empty block + "Clear search".

## State Management
Per-view state (lift to services/NgRx as suits):
- `theme` (persisted), `locale` (default `en`).
- `activeBillId`, fetched bill object.
- **AI chat:** `messages[]` (role, text, cites[]), `typing` flag, input ref.
- **Commons:** `query`, `answer` (lead + hits), `level`, `status`, `category`.
- **Congress:** `query`.
- **Peer review (if you keep the workspace):** per-criterion scores, expanded questions, recommendation, submitted flag.
Data fetching: `GET /api/bills`, `/api/bills/{id}`, `/api/studies`, `/api/commons/search`, `/api/commons/ask`, `/api/legislation`, `POST /api/datasets` (upload), `POST /api/ai/chat`.

---

## Design Tokens

**Typography**
- Serif (headings, titles): **Spectral** — 400/500/600/700, incl. italics. Tight tracking on large headings (~-0.01 to -0.015em).
- Sans (body/UI): **Public Sans** — 400/500/600/700.
- Mono (refs, IDs, dates, votes): **Roboto Mono** — 400/500.
- Icons: **Material Icons** (filled). Category→icon and chamber icons noted above.
- Scale: H1 38–50px, section H2 26–32px, card/title 18–26px, body 14–17px, meta 11–13px. Overlines 11–12px 700 uppercase, letter-spacing .1–.14em.

**Color — Light**
- Surfaces: `--bg #f7f4ee`, `--surface #ffffff`, `--surface-2 #faf8f3`.
- Ink/text: `--ink #1c1814`, `--text #2c2720`, `--text-2 #4a4339`, `--text-3 #6f665a`, `--text-4 #9a8e7a`.
- Borders: `--border #e2dccf`, `--border-2 #d8d2c4`, `--border-3 #f0ebe1`.
- Accent (crimson): `--accent #7b1113`, `--accent-fill #7b1113`, `--accent-soft #faf0f0`.
- Gold: `--gold #9a7b2e`. Dot `--dot #bcae97`.
- Inverse (dark bands): `--inverse #1c1814`, `--inverse-2 #14110f`, text on it `--on-inverse #ffffff`, `--on-inverse-2 #cfc7b8`, `--on-inverse-3 #8a8270`.
- Semantic: ok `#2e7d4f` on `#e6f0ea`; warn `#b5751d` on `#fbf0dd`; alert `#c0561f` on `#fbe7dc`; reject `#c0392b`; neutral `#6f665a` on `#efece4`.

**Color — Dark** (`[data-theme="dark"]`)
- `--bg #141109`, `--surface #211d15`, `--surface-2 #1a1710`.
- `--ink #f1ebdd`, `--text #e7e0d1`, `--text-2 #c9bfac`, `--text-3 #a89e8a`, `--text-4 #857c69`.
- `--border #322c22`, `--border-2 #3d362a`, `--border-3 #272219`.
- `--accent #e8766c` (brightened for contrast), `--accent-fill #8c1618`, `--accent-soft #37191a`, `--gold #cda85a`.
- `--inverse #27221a`, `--inverse-2 #0f0d08`; semantic colors brighten with translucent tinted backgrounds (ok `#5cc488`, warn `#e3ad55`, alert `#e8916a`, reject `#e8766c`).
- Full pairs are in the prototype `:root` / `[data-theme="dark"]` blocks — copy verbatim.

**Spacing & layout**
- Content column max **1240px**, side padding **32px**; section vertical padding 44–72px.
- ~8px rhythm; card padding 18–28px.
- Detail rails **300–320px**, library filter rail **248px**, sticky offset `top: 90px`.

**Radii / shadows / motion**
- Radii: this product uses **crisp 2px** on cards/buttons/pills (chips/badges 12–20px pill). (Contrast with Dash Support's softer 14–16px — do not import those here.)
- Borders do most of the work; shadows are minimal (light `0 1px 8px rgba(28,24,20,.05)` on the sticky nav).
- Motion 150–200ms ease; hover tint to crimson; no decorative animation.

## Assets
- **No photography/illustration.** All iconography is **Material Icons** (webfont ligatures). Fonts load from Google Fonts (Spectral, Public Sans, Roboto Mono, Material Icons). No custom logo file — the wordmark is a CSS "CR" monogram in a double-ring circle; swap for a real mark when available.

## Files
- `prototype/Housing Policy Review.dc.html` — the full interactive prototype (all screens). Open in a normal browser to view (it references a runtime that is not included, so treat it as the visual/behavioral spec; read the source for exact markup, tokens, and logic).
- `prototype/data/bill-review.schema.json` — **the bill-review data contract** (JSON Schema 2020-12). Drive your Postgres model + API DTOs + validation from this.
- `prototype/data/bills.json` — worked example (H.R. 6644) + the store shape (`featuredBillId`, `bills{}`). Seed data.
- `prototype/prompts/bill-review-generation.md` — the **Claude request** (system + user prompt, i18n rules, validate→merge pipeline) your .NET service issues to generate reviews.

## Suggested build order
1. DB schema from `bill-review.schema.json` (bills + JSONB content, `commons_entries` with `vector` column, studies, legislation, dataset_submissions). Enable `pgvector`.
2. API: `GET /bills`, `/bills/{id}`, `/legislation`, `/commons/search`; seed from `bills.json`.
3. Angular shell + tokens (crimson system + dark mode) → Home, Bill Review (data-driven), Studies Library.
4. Commons search (SQL filter) → then **Ask** (pgvector retrieval + Claude) and the AI chat.
5. Congress tracker + Congress.gov ingestion job; member dataset upload + review workflow.
6. Wire the generation prompt as a server job; validate output against the schema before persisting.
