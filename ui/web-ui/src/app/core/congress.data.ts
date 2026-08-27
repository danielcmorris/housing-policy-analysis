/* Federal legislation tracker tiles, ported from the prototype. Production
   replaces this with a `legislation` table synced from Congress.gov via the
   existing law-retrieval API. */

export interface CongressBill {
  ref: string; congress: string; title: string; chamber: 'House' | 'Senate';
  status: string; statusKey: 'advancing' | 'committee' | 'introduced';
  category: string; updated: string; sponsor: string; summary: string; featured?: boolean;
}

export const CONGRESS_BILLS: CongressBill[] = [
  { ref: 'H.R. 6644', congress: '119th', title: '21st Century ROAD to Housing Act', chamber: 'House', status: 'Senate Passed', statusKey: 'advancing', category: 'Housing Supply', updated: 'Mar 12, 2026', sponsor: 'Rep. French Hill (R-AR)', summary: 'Wide-ranging supply bill: streamlines federal environmental review, modernizes manufactured housing, expands affordable-housing finance, and reforms rural programs. Passed House 390–9, Senate 89–10.', featured: true },
  { ref: 'S. 2145', congress: '119th', title: 'Housing Supply Expansion Act', chamber: 'Senate', status: 'In Committee', statusKey: 'committee', category: 'Housing Supply', updated: 'Feb 28, 2026', sponsor: 'Sen. R. Ellison (D-NV)', summary: 'Conditions a share of federal transportation funds on state adoption of by-right multifamily zoning near transit corridors.' },
  { ref: 'H.R. 4820', congress: '119th', title: 'Yes In My Backyard (YIMBY) Act Reauthorization', chamber: 'House', status: 'Introduced', statusKey: 'introduced', category: 'Regulation Reform', updated: 'Feb 19, 2026', sponsor: 'Rep. D. Okafor (D-OR)', summary: 'Requires CDBG grantees to report on and remove discriminatory or exclusionary land-use policies as a condition of funding.' },
  { ref: 'S. 1877', congress: '119th', title: 'Affordable Housing Credit Improvement Act', chamber: 'Senate', status: 'In Committee', statusKey: 'committee', category: 'Finance', updated: 'Feb 11, 2026', sponsor: 'Sen. M. Cardwell (D-MD)', summary: 'Expands the Low-Income Housing Tax Credit allocation and lowers the bond-financing threshold to unlock additional affordable units.' },
  { ref: 'H.R. 5390', congress: '119th', title: 'Manufactured Housing Modernization Act', chamber: 'House', status: 'Reported', statusKey: 'advancing', category: 'Regulation Reform', updated: 'Jan 30, 2026', sponsor: 'Rep. L. Vance (R-TX)', summary: 'Updates HUD manufactured-housing construction standards and preempts local bans on titled factory-built homes on residential lots.' },
  { ref: 'S. 2560', congress: '119th', title: 'Tenant Protection & Rental Stability Act', chamber: 'Senate', status: 'Introduced', statusKey: 'introduced', category: 'Rent Regulation', updated: 'Jan 22, 2026', sponsor: 'Sen. A. Reyes (D-CA)', summary: 'Establishes federal notice and just-cause standards for federally backed rental housing and funds state rental-assistance pilots.' },
];

export const CONGRESS_STATUS_STYLE: Record<string, { color: string; bg: string }> = {
  advancing:  { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  committee:  { color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  introduced: { color: 'var(--text-3)', bg: 'var(--neutral-bg)' },
};

export function filterCongress(query: string): CongressBill[] {
  const q = (query || '').toLowerCase().trim();
  const tokens = q.split(/[^a-z0-9]+/).filter((w) => w.length > 1);
  if (!tokens.length) return CONGRESS_BILLS;
  return CONGRESS_BILLS.filter((b) => {
    const hay = (b.title + ' ' + b.ref + ' ' + b.category + ' ' + b.summary + ' ' + b.sponsor + ' ' + b.status).toLowerCase();
    return tokens.some((t) => hay.includes(t));
  });
}
