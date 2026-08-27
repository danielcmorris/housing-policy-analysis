/* The studies-under-review corpus, ported from the prototype. Replaced by
   GET /api/studies in production. */

export interface Study {
  ref: string; category: string; title: string; authors: string; year: number;
  pages: number; reviews: number; clarity: number; status: string; excerpt: string;
}

export const STUDY_STATUS: Record<string, { color: string; bg: string }> = {
  'Peer Reviewed':     { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  'In Review':         { color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  'Awaiting Response': { color: 'var(--alert)',  bg: 'var(--alert-bg)' },
  'Submitted':         { color: 'var(--text-3)', bg: 'var(--neutral-bg)' },
};

export function clarityColor(n: number): string {
  return n >= 8.5 ? 'var(--ok)' : (n >= 7.5 ? 'var(--warn)' : 'var(--alert)');
}

export const ALL_STUDIES: Study[] = [
  { ref: 'CUHPR-2026-0142', category: 'Supply-Side Economics', title: 'The Supply Response to Zoning Reform: Evidence from Minneapolis 2040', authors: 'University of Chicago & MIT', year: 2026, pages: 88, reviews: 5, clarity: 9.2, status: 'Peer Reviewed', excerpt: 'A synthetic-control evaluation finds the elimination of single-family-only zoning raised permitted units roughly 12% over four years, concentrated in higher-amenity tracts.' },
  { ref: 'CUHPR-2024-0071', category: 'Rent Control', title: 'The Economic Effects of Rent Regulation: A National Assessment', authors: 'National Bureau of Economic Research', year: 2024, pages: 312, reviews: 4, clarity: 8.9, status: 'Peer Reviewed', excerpt: 'A comprehensive synthesis of forty years of rent-regulation evidence across U.S. jurisdictions and its effects on supply, mobility, and welfare.' },
  { ref: 'CUHPR-2025-0918', category: 'Rent Control', title: 'Rent Stabilization and Housing Quality: A Twelve-City Panel', authors: 'RAND Corporation', year: 2025, pages: 268, reviews: 4, clarity: 8.7, status: 'In Review', excerpt: 'Panel evidence on whether stabilization ordinances correlate with deferred maintenance and unit-quality decline over a fifteen-year window.' },
  { ref: 'CUHPR-2025-0140', category: 'Regulation Reductions', title: 'Minimum Lot Size Reform and Housing Production', authors: 'Mercatus Center, George Mason University', year: 2025, pages: 196, reviews: 3, clarity: 8.8, status: 'Peer Reviewed', excerpt: 'Finds reductions in minimum lot sizes lowered per-unit land costs by roughly 18% in affected tracts, with the largest effects where prior minimums most bound.' },
  { ref: 'CUHPR-2025-0112', category: 'Building Policies', title: 'Housing Supply and the Cost of Residential Construction', authors: 'U.S. Dept. of Housing & Urban Development (PD&R)', year: 2025, pages: 248, reviews: 3, clarity: 8.2, status: 'In Review', excerpt: 'A federal assessment linking regulatory burden, material costs, and labor constraints to the per-unit cost of new residential construction.' },
  { ref: 'CUHPR-2026-0377', category: 'Regulation Reductions', title: 'Land-Use Deregulation and Regional Housing Affordability', authors: 'Brookings Institution', year: 2026, pages: 204, reviews: 3, clarity: 8.5, status: 'Peer Reviewed', excerpt: 'Estimates how metropolitan-scale land-use liberalization propagates into regional rents and price-to-income ratios.' },
  { ref: 'CUHPR-2025-0166', category: 'Building Policies', title: 'Inclusionary Zoning and Market-Rate Housing Production', authors: 'Furman Center, New York University', year: 2025, pages: 142, reviews: 3, clarity: 8.0, status: 'In Review', excerpt: 'Tests whether inclusionary mandates suppress overall market-rate housing starts and how outcomes vary with the stringency of the set-aside.' },
  { ref: 'CUHPR-2024-0203', category: 'Gentrification', title: 'Transit Expansion, Gentrification, and Displacement', authors: 'Urban Institute', year: 2024, pages: 220, reviews: 4, clarity: 8.4, status: 'Awaiting Response', excerpt: 'Uses staggered transit openings to identify displacement effects on incumbent low-income renters and the durability of neighborhood change.' },
  { ref: 'CUHPR-2024-0088', category: 'Filtering', title: 'Filtering and the Lifecycle of the American Housing Stock', authors: 'Joint Center for Housing Studies, Harvard', year: 2024, pages: 174, reviews: 2, clarity: 8.1, status: 'Peer Reviewed', excerpt: 'Estimates the rate at which units filter down the income distribution as the stock ages, and the reinvestment that arrests it.' },
  { ref: 'CUHPR-2025-0451', category: 'Costs', title: 'Housing Cost Burden Across the Income Distribution', authors: 'The Pew Charitable Trusts', year: 2025, pages: 156, reviews: 2, clarity: 7.6, status: 'Submitted', excerpt: 'Maps housing cost-burden incidence across income deciles in sixty metropolitan areas and the role of supply constraints.' },
];

export const TOPICS = ['Rent Control', 'Building Policies', 'Regulation Reductions', 'Filtering', 'Supply-Side Economics', 'Gentrification', 'Costs'];

export const TOPIC_FILTERS: { label: string; count: number }[] = [
  { label: 'Rent Control', count: 2 }, { label: 'Building Policies', count: 2 }, { label: 'Regulation Reductions', count: 2 },
  { label: 'Filtering', count: 1 }, { label: 'Supply-Side Economics', count: 1 }, { label: 'Gentrification', count: 1 }, { label: 'Costs', count: 1 },
];

export const STATUS_FILTERS: { label: string; color: string }[] = [
  { label: 'Peer Reviewed', color: 'var(--ok)' }, { label: 'In Review', color: 'var(--warn)' },
  { label: 'Awaiting Response', color: 'var(--alert)' }, { label: 'Submitted', color: 'var(--text-3)' },
];
