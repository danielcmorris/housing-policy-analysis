/* The Data Commons demo corpus and canned "Ask" routing, ported from the
   prototype. Production replaces this with /api/commons/search (SQL filters)
   and /api/commons/ask (pgvector retrieval + Claude). */

export interface CommonsEntry {
  ref: string; title: string; level: 'federal' | 'state' | 'city'; place: string;
  year: number; status: 'passed' | 'failed'; category: string; tags: string[];
  summary: string; bill?: boolean;
}

export const COMMONS_CORPUS: CommonsEntry[] = [
  { ref: 'US-HR-6644-119', title: '21st Century ROAD to Housing Act', level: 'federal', place: 'United States', year: 2026, status: 'passed', category: 'Supply-Side', tags: ['Zoning', 'Environmental Review', 'Manufactured Housing'], summary: 'Wide-ranging federal supply bill; passed House 390–9 and Senate 89–10.', bill: true },
  { ref: 'CA-SB-9-2021', title: 'California Housing Opportunity & More Efficiency (HOME) Act', level: 'state', place: 'California', year: 2021, status: 'passed', category: 'Regulation Reduction', tags: ['Zoning', 'Lot Splits', 'Duplex'], summary: 'Allowed duplexes and lot splits on single-family parcels statewide.' },
  { ref: 'OR-HB-2001-2019', title: 'Oregon Middle Housing Act', level: 'state', place: 'Oregon', year: 2019, status: 'passed', category: 'Supply-Side', tags: ['Zoning', 'Missing Middle'], summary: 'Legalized duplexes–fourplexes in cities above population thresholds.' },
  { ref: 'MN-MPLS-2040-2019', title: 'Minneapolis 2040 Comprehensive Plan', level: 'city', place: 'Minneapolis, MN', year: 2019, status: 'passed', category: 'Supply-Side', tags: ['Zoning', 'Upzoning'], summary: 'Eliminated single-family-only zoning citywide.' },
  { ref: 'NY-S-6458-2019', title: 'NY Housing Stability & Tenant Protection Act', level: 'state', place: 'New York', year: 2019, status: 'passed', category: 'Rent Regulation', tags: ['Rent Control', 'Tenant Protection'], summary: 'Strengthened and expanded rent-stabilization statewide.' },
  { ref: 'CA-AB-1482-2019', title: 'California Tenant Protection Act', level: 'state', place: 'California', year: 2019, status: 'passed', category: 'Rent Regulation', tags: ['Rent Cap', 'Just Cause'], summary: 'Statewide rent cap and just-cause eviction standard.' },
  { ref: 'WA-HB-1110-2023', title: 'Washington Middle Housing Bill', level: 'state', place: 'Washington', year: 2023, status: 'passed', category: 'Supply-Side', tags: ['Zoning', 'Missing Middle'], summary: 'Required cities to allow two–six units on residential lots.' },
  { ref: 'FL-SB-102-2023', title: 'Florida Live Local Act', level: 'state', place: 'Florida', year: 2023, status: 'passed', category: 'Affordability', tags: ['Preemption', 'Tax Incentive'], summary: 'Preempted local rules and added incentives for affordable development.' },
  { ref: 'TX-HB-2989-2023', title: 'Texas Statewide Zoning Preemption', level: 'state', place: 'Texas', year: 2023, status: 'failed', category: 'Regulation Reduction', tags: ['Zoning', 'Preemption'], summary: 'Would have curbed municipal minimum-lot-size rules; died in committee.' },
  { ref: 'CA-SB-827-2018', title: 'California Transit-Density Bill', level: 'state', place: 'California', year: 2018, status: 'failed', category: 'Supply-Side', tags: ['Zoning', 'Transit', 'Upzoning'], summary: 'Transit-oriented upzoning; failed its first committee vote.' },
  { ref: 'MA-STL-2020', title: 'Massachusetts Rent-Control Reauthorization', level: 'state', place: 'Massachusetts', year: 2020, status: 'failed', category: 'Rent Regulation', tags: ['Rent Control'], summary: 'Sought to re-legalize local rent control; not enacted.' },
  { ref: 'CO-HB-1313-2023', title: 'Colorado Land Use Initiative', level: 'state', place: 'Colorado', year: 2023, status: 'failed', category: 'Supply-Side', tags: ['Zoning', 'Preemption'], summary: 'Statewide land-use reform; failed after senate amendments.' },
  { ref: 'IL-CHI-ARO-2021', title: 'Chicago Affordable Requirements Ordinance', level: 'city', place: 'Chicago, IL', year: 2021, status: 'passed', category: 'Affordability', tags: ['Inclusionary Zoning', 'Set-Aside'], summary: 'Updated inclusionary set-aside and in-lieu fee schedule.' },
  { ref: 'TX-HOU-LOT-1998', title: 'Houston Minimum Lot Size Reform', level: 'city', place: 'Houston, TX', year: 1998, status: 'passed', category: 'Regulation Reduction', tags: ['Lot Size', 'Deregulation'], summary: 'Cut minimum lot sizes, enabling townhouse infill.' },
  { ref: 'OR-PDX-IH-2017', title: 'Portland Inclusionary Housing Program', level: 'city', place: 'Portland, OR', year: 2017, status: 'passed', category: 'Affordability', tags: ['Inclusionary Zoning'], summary: 'Mandatory affordable set-asides on larger projects.' },
  { ref: 'NY-NYC-421a-2022', title: 'NYC 421-a Tax Incentive Renewal', level: 'city', place: 'New York, NY', year: 2022, status: 'failed', category: 'Affordability', tags: ['Tax Incentive'], summary: 'Renewal of the multifamily tax exemption; lapsed without agreement.' },
  { ref: 'CA-SF-VAC-2022', title: 'San Francisco Vacancy Tax', level: 'city', place: 'San Francisco, CA', year: 2022, status: 'passed', category: 'Rent Regulation', tags: ['Vacancy Tax'], summary: 'Tax on long-vacant residential units.' },
  { ref: 'MN-MPLS-RENT-2021', title: 'Minneapolis Rent Stabilization Authorization', level: 'city', place: 'Minneapolis, MN', year: 2021, status: 'passed', category: 'Rent Regulation', tags: ['Rent Control', 'Ballot'], summary: 'Ballot measure authorizing a future rent-control ordinance.' },
];

const STOP = new Set(['the','and','for','that','with','from','why','how','what','which','does','did','are','was','were','have','has','our','they','them','most','more','than','vs','bill','bills','policy','policies','measure','measures','government','produce','produced','compare']);

export function filterCommons(query: string, level: string, status: string, category: string): CommonsEntry[] {
  const raw = (query || '').toLowerCase().trim();
  const tokens = raw.split(/[^a-z]+/).filter((w) => w.length > 2 && !STOP.has(w));
  return COMMONS_CORPUS.filter((b) => {
    if (level !== 'all' && b.level !== level) return false;
    if (status !== 'all' && b.status !== status) return false;
    if (category !== 'all' && b.category !== category) return false;
    if (tokens.length) {
      const hay = (b.title + ' ' + b.place + ' ' + b.summary + ' ' + b.category + ' ' + b.tags.join(' ')).toLowerCase();
      if (!tokens.some((t) => hay.includes(t))) return false;
    }
    return true;
  });
}

export function commonsAnswerLead(q: string): string {
  const t = q.toLowerCase();
  if (t.includes('rent')) return 'Across the Commons, rent-regulation measures cluster in high-cost coastal markets. Stabilization laws that passed (NY 2019, CA AB-1482) tended to bind on the maintenance and turnover margins; several re-authorization attempts (MA 2020) failed. Evidence links stabilization to quality effects more than to new supply.';
  if (t.includes('fail') || t.includes('why')) return 'Failed measures in the corpus share a pattern: broad statewide preemption of local zoning (TX 2023, CO 2023, CA SB-827) draws municipal opposition and dies in committee, while narrower, incentive-based bills more often pass. Preemption scope is the strongest predictor of failure here.';
  if (t.includes('zon') || t.includes('supply') || t.includes('upzon')) return 'Supply-side upzoning has the strongest passage record when it sets a floor cities must meet (OR 2019, WA 2023, MN 2019) rather than overriding local control wholesale. Realized effects are positive but lagged — permits respond in 1–2 years, completions in 3–5.';
  return 'Here are the closest matches in the Commons for your question. The record spans federal, state, and city measures — both enacted and failed — cross-referenced by mechanism, place, and outcome so you can compare like with like.';
}
