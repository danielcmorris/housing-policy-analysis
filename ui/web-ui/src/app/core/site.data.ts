/* Static site copy shared across screens, ported from the prototype. */

export const METHOD_STEPS = [
  { num: '01', icon: 'account_tree', title: 'Decompose the Provisions', body: 'Each proposal is broken down into its primary, testable provisions, so the analysis turns on specific mechanisms rather than political framing.' },
  { num: '02', icon: 'history', title: 'Match Historical Precedent', body: 'We map each provision against five decades of comparable policy — distinguishing what passed from what failed, and which enacted measures most closely mirror the bill at hand.' },
  { num: '03', icon: 'timeline', title: 'Trace Realized Effects', body: 'For the precedents that became law, we trace the documented short- and long-term outcomes: how quickly effects appeared, how large they were, and whether they endured.' },
  { num: '04', icon: 'insights', title: 'Project the Likely Outcome', body: 'Grounded in that evidentiary record, we set out what can reasonably be expected from the proposal under review — and the conditions on which that expectation depends.' },
];

export const TECH_PILLARS = [
  { icon: 'storage', title: 'Thousands of bills & proposals', body: 'Legislation and policy proposals from federal, state, and city governments — catalogued, normalized, and continuously expanded.' },
  { icon: 'hub', title: 'Cross-referenced vector database', body: 'Each measure is embedded and linked by mechanism, jurisdiction, and intent, so comparable policies surface by meaning — not by keyword.' },
  { icon: 'query_stats', title: 'Time-matched economic record', body: 'Regional economic conditions for the corresponding period sit beside each policy, so its outcomes are read in their true historical context.' },
];

export const COMMENTARY = [
  { date: 'Jun 18, 2026', tag: 'Supply-Side Economics', title: 'What Minneapolis tells us — and what it does not — about upzoning', author: 'Elena Hargrove' },
  { date: 'Jun 11, 2026', tag: 'Rent Control', title: 'The maintenance margin: how stabilization reshapes landlord behavior', author: 'Dana Okonkwo' },
  { date: 'Jun 04, 2026', tag: 'Methodology', title: 'Why we attach an AI methodology note to every study', author: 'The Review Board' },
];

export const PRINCIPLES = [
  { icon: 'balance', title: 'Non-partisan', body: 'We evaluate evidence, not ideology. Findings are reported wherever they fall, and our funding is fully disclosed.' },
  { icon: 'visibility', title: 'Transparent', body: 'Every study carries its data sources, methods, AI analysis, and the full peer-review record alongside it.' },
  { icon: 'school', title: 'Rigorous', body: 'Studies pass a fixed-criteria review by independent fellows before they receive the Center’s imprimatur.' },
];

export const PEOPLE = [
  { initials: 'EH', name: 'Dr. Elena Hargrove', role: 'Director', bg: 'var(--accent-fill)', bio: 'Urban economics; land-use regulation.' },
  { initials: 'PN', name: 'Dr. Paul Nakamura', role: 'Chief Methodologist', bg: 'var(--inverse)', bio: 'Causal inference; synthetic controls.' },
  { initials: 'DO', name: 'Dr. Dana Okonkwo', role: 'Senior Fellow', bg: '#9a7b2e', bio: 'Rental markets; housing quality.' },
  { initials: 'LT', name: 'Dr. Lan Tran', role: 'Senior Fellow', bg: '#5c0c0d', bio: 'Deregulation; supply elasticity.' },
];

export const RESOURCE_GROUPS = [
  { label: 'Federal Legislation & Regulation', icon: 'account_balance', links: [
    { name: 'Congress.gov', desc: 'The official source for federal bills, public laws, and the Congressional Record.', url: 'https://www.congress.gov', domain: 'congress.gov' },
    { name: 'GovInfo (U.S. GPO)', desc: 'Authenticated federal government publications, reports, and documents.', url: 'https://www.govinfo.gov', domain: 'govinfo.gov' },
    { name: 'Regulations.gov', desc: 'Proposed federal rules and the full public-comment record.', url: 'https://www.regulations.gov', domain: 'regulations.gov' },
    { name: 'Federal Register', desc: 'The daily journal of the United States government.', url: 'https://www.federalregister.gov', domain: 'federalregister.gov' },
  ] },
  { label: 'State & Local Policy', icon: 'location_city', links: [
    { name: 'LegiScan', desc: 'Real-time bill tracking across all fifty state legislatures.', url: 'https://legiscan.com', domain: 'legiscan.com' },
    { name: 'NCSL', desc: 'National Conference of State Legislatures — research and policy databases.', url: 'https://www.ncsl.org', domain: 'ncsl.org' },
    { name: 'Municode Library', desc: 'Searchable municipal codes and local ordinances nationwide.', url: 'https://library.municode.com', domain: 'library.municode.com' },
    { name: 'Local Housing Solutions', desc: 'Evidence-based policy toolkit for city and county housing strategy.', url: 'https://localhousingsolutions.org', domain: 'localhousingsolutions.org' },
  ] },
  { label: 'Legal Research', icon: 'gavel', links: [
    { name: 'Cornell Legal Information Institute', desc: 'Free access to U.S. statutes, regulations, and case law.', url: 'https://www.law.cornell.edu', domain: 'law.cornell.edu' },
    { name: 'CourtListener', desc: 'The Free Law Project’s database of court opinions and dockets.', url: 'https://www.courtlistener.com', domain: 'courtlistener.com' },
    { name: 'Justia', desc: 'Free case law, statutory codes, and legal resources.', url: 'https://www.justia.com', domain: 'justia.com' },
    { name: 'Google Scholar', desc: 'Search across case law and scholarly literature.', url: 'https://scholar.google.com', domain: 'scholar.google.com' },
  ] },
  { label: 'Academic Research & Economic Data', icon: 'school', links: [
    { name: 'NBER', desc: 'National Bureau of Economic Research working papers.', url: 'https://www.nber.org', domain: 'nber.org' },
    { name: 'SSRN', desc: 'Social Science Research Network preprints and papers.', url: 'https://www.ssrn.com', domain: 'ssrn.com' },
    { name: 'HUD USER (PD&R)', desc: 'Federal housing research, data sets, and policy reports.', url: 'https://www.huduser.gov', domain: 'huduser.gov' },
    { name: 'Harvard Joint Center for Housing Studies', desc: 'Leading academic research on U.S. housing markets.', url: 'https://www.jchs.harvard.edu', domain: 'jchs.harvard.edu' },
    { name: 'FRED (St. Louis Fed)', desc: 'Federal Reserve economic data — hundreds of thousands of series.', url: 'https://fred.stlouisfed.org', domain: 'fred.stlouisfed.org' },
    { name: 'U.S. Census Bureau', desc: 'American Community Survey and national housing statistics.', url: 'https://www.census.gov', domain: 'census.gov' },
  ] },
];

export const FOOTER_COLS = [
  { head: 'Research', links: ['Studies Library', 'Working Papers', 'Research Programs', 'Data & Replication'] },
  { head: 'Tools', links: ['Research Assistant', 'Data Commons', 'Clarity Scores', 'Citation Index'] },
  { head: 'Institute', links: ['About', 'Fellows', 'Research Standards', 'Contact'] },
];

export const DC_STATS = [
  { value: '2.4M+', label: 'Policy Records' },
  { value: '9,200', label: 'Jurisdictions' },
  { value: '140+', label: 'Data Sources' },
  { value: 'Daily', label: 'Ingestion Cycle' },
];

export const DC_COVERAGE = [
  { icon: 'account_balance', label: 'Federal', detail: 'Bills, statutes & agency rules', count: '180K' },
  { icon: 'gavel', label: 'State', detail: '50 states & territories', count: '610K' },
  { icon: 'location_city', label: 'Local', detail: 'County & municipal ordinances', count: '1.4M' },
  { icon: 'query_stats', label: 'Economic Series', detail: 'Matched regional indicators', count: '240K' },
];

export const DC_AXES = [
  { icon: 'category', title: 'By policy mechanism', body: 'Zoning, rent regulation, subsidy, tax, permitting — grouped by what the policy actually does, not how it is named.' },
  { icon: 'public', title: 'By jurisdiction', body: 'Federal, state, county, and city, with the demographic and market context of each place attached.' },
  { icon: 'history', title: 'By time period', body: 'Roughly fifty years of record, so a measure can be compared across eras as well as across places.' },
  { icon: 'trending_up', title: 'By economic condition', body: 'Linked to the housing-market and macro indicators prevailing when each policy took effect.' },
  { icon: 'rule', title: 'By observed outcome', body: 'Connected to the empirical findings on what followed — supply, rents, quality, displacement.' },
  { icon: 'hub', title: 'By similarity', body: 'Vector-indexed so a new proposal surfaces its nearest historical analogues automatically.' },
];

export const DC_STEPS = [
  { num: 'STEP 01', title: 'Submit', body: 'Upload your dataset with a short description of its source, coverage, and method.' },
  { num: 'STEP 02', title: 'Provenance check', body: 'Our data team verifies origin, licensing, and documentation completeness.' },
  { num: 'STEP 03', title: 'Methodological review', body: 'A fellow assesses construction and fitness for comparative use, and may request clarifications.' },
  { num: 'STEP 04', title: 'Cross-reference & publish', body: 'Accepted data is indexed along every axis above and credited to you in the Commons.' },
];

export const AI_CAPABILITIES = [
  { icon: 'summarize', title: 'Summarize findings', body: 'Condense any study to its core results and confidence intervals.' },
  { icon: 'fact_check', title: 'Critique methodology', body: 'Surface identification risks, robustness gaps, and data limitations.' },
  { icon: 'quiz', title: 'Draft review questions', body: 'Generate targeted questions for the peer-review panel.' },
  { icon: 'hub', title: 'Cross-reference evidence', body: 'Connect findings across studies and research programs.' },
];

export interface ChatCite { ref: string; title: string; }
export interface ChatMessage { role: 'user' | 'ai'; text: string; cites?: ChatCite[]; }

export const INITIAL_MESSAGES: ChatMessage[] = [
  { role: 'user', text: 'Summarize the key findings of the Minneapolis 2040 supply study and flag any methodological weaknesses.' },
  { role: 'ai', text: 'The study finds that eliminating single-family-only zoning raised permitted units by an estimated 12.4% over four years, with effects concentrated in higher-amenity tracts and no significant short-run rent effect.\n\nTwo methodological cautions:\n\n1. The synthetic control is sensitive to the inclusion of two high-growth Sun Belt metros — a leave-one-out panel would strengthen the result.\n\n2. The four-year window may be too short to observe the full rent response; supply lags entitlement and financing by several years.',
    cites: [
      { ref: 'CUHPR-2026-0142', title: 'The Supply Response to Zoning Reform' },
      { ref: 'CUHPR-2025-0140', title: 'Minimum Lot Size Reform and Housing Production' },
    ] },
];

/* Keyword-routed canned replies from the prototype; production streams from
   Claude with retrieval over the reviewed corpus. */
export function aiReply(text: string): ChatMessage {
  const t = text.toLowerCase();
  if (t.includes('compare') || t.includes('cross')) {
    return { role: 'ai', text: 'Across the rent-stabilization and lot-size studies, the evidence diverges on supply elasticity. The minimum-lot-size reform study shows a fast, large supply response; the twelve-city rent-stabilization panel shows quality erosion but no supply effect — consistent with stabilization binding on the intensive (maintenance) margin rather than the extensive (construction) margin.',
      cites: [ { ref: 'CUHPR-2025-0918', title: 'Rent Stabilization and Housing Quality' }, { ref: 'CUHPR-2025-0140', title: 'Minimum Lot Size Reform and Housing Production' } ] };
  }
  if (t.includes('peer') || t.includes('question')) {
    return { role: 'ai', text: 'Suggested peer-review questions for the Filtering study:\n\n1. How is the hazard rate of unit downward-filtering identified separately from cohort depreciation?\n2. Does the model account for renovation/reinvestment that reverses filtering?\n3. Are the metro fixed effects absorbing the very variation of interest?\n\nI can attach these to the review workspace.',
      cites: [ { ref: 'CUHPR-2024-0088', title: 'Filtering and the Lifecycle of the American Housing Stock' } ] };
  }
  if (t.includes('lot') || t.includes('cost')) {
    return { role: 'ai', text: 'On minimum lot sizes and costs: the reform study estimates that reducing minimum lot sizes lowered per-unit land costs by roughly 18% in affected tracts, with the largest effects where prior minimums were most binding. The cost-burden assessment suggests these savings only partly pass through to renters in supply-constrained submarkets.',
      cites: [ { ref: 'CUHPR-2025-0140', title: 'Minimum Lot Size Reform and Housing Production' }, { ref: 'CUHPR-2025-0451', title: 'Housing Cost Burden Across the Income Distribution' } ] };
  }
  return { role: 'ai', text: 'Based on the reviewed corpus, the strongest evidence points to land-use liberalization producing measurable but lagged supply responses, with distributional effects that depend heavily on local land values and amenity levels. I can pull the specific studies, critique a methodology, or draft peer-review questions — just say which.',
    cites: [ { ref: 'CUHPR-2026-0142', title: 'The Supply Response to Zoning Reform' } ] };
}
