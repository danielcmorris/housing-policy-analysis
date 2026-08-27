import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CONGRESS_BILLS } from './congress.data';

/* Live legislation tracker feed from the law-retrieval API (GET /legislation),
   which serves the housing bills maintained in Postgres by the congress.gov
   sync. Falls back to the static demo tiles if the API is unreachable. */

/* HousingPolicy.Api (ASP.NET Core) — run with:
   dotnet run --project HousingPolicy.Api --urls http://localhost:5000 */
export const API_BASE = 'http://localhost:5000/api';

export interface TrackerBill {
  bill_id: string;
  tracking_status?: string;
  has_text?: boolean;
  tags?: string[];
  tags_source?: string | null;
  watch?: boolean;
  display_date?: string | null;
  displayed?: boolean;
  pinned?: boolean;
  ref: string;
  congress: string;
  chamber: 'House' | 'Senate' | string;
  title: string;
  status_key: string;
  status_text: string | null;
  updated: string | null;
  introduced: string | null;
  category: string;
  sponsor: string | null;
  sponsor_party?: string | null;
  sponsor_state?: string | null;
  summary: string | null;
  congress_gov_url: string;
}

export const STATUS_LABEL: Record<string, string> = {
  enacted: 'Enacted',
  to_president: 'To President',
  advancing: 'Advancing',
  committee: 'In Committee',
  introduced: 'Introduced',
  failed: 'Failed',
};

/* Legislative pipeline stages in order, with a one-sentence meaning and a
   longer hover explanation. 'failed' is terminal and can occur at any point. */
export const STATUS_INFO: {
  key: string; order: string; label: string; sentence: string; detail: string;
}[] = [
  {
    key: 'introduced', order: '1', label: 'Introduced',
    sentence: 'The bill has been formally filed in its chamber.',
    detail: 'Step 1 — A member has filed the bill; it has a number and text but no committee action yet. Most bills never advance beyond this stage.',
  },
  {
    key: 'committee', order: '2', label: 'In Committee',
    sentence: 'Referred to a committee for study, hearings, and markup.',
    detail: 'Step 2 — The bill is before a committee (e.g., House Financial Services), which can hold hearings, amend it, and vote on whether to send it to the full chamber. Most bills die in committee.',
  },
  {
    key: 'advancing', order: '3', label: 'Advancing',
    sentence: 'Reported out of committee or passed by at least one chamber.',
    detail: 'Step 3 — The bill is moving: reported to the floor, placed on a calendar, or passed by one chamber and awaiting the other. Both chambers must pass identical text before it can go to the President.',
  },
  {
    key: 'to_president', order: '4', label: 'To President',
    sentence: 'Passed both chambers and awaiting the President’s signature.',
    detail: 'Step 4 — Congress has agreed on final text and presented it to the President, who may sign it, veto it, or let it become law without a signature after ten days.',
  },
  {
    key: 'enacted', order: '5', label: 'Enacted',
    sentence: 'Signed into law (or a veto overridden); now a public law.',
    detail: 'Step 5 — The bill has become law and receives a Public Law number. Federal agencies then implement it through rules, funding, and programs.',
  },
  {
    key: 'failed', order: '×', label: 'Failed',
    sentence: 'Vetoed or definitively defeated — can happen at any stage.',
    detail: 'Terminal — The bill was vetoed without an override or failed a decisive vote. The same policy may return as a new bill in a later Congress.',
  },
];

export const STATUS_DETAIL: Record<string, string> = Object.fromEntries(
  STATUS_INFO.map((s) => [s.key, s.detail]),
);

export const STATUS_STYLE: Record<string, { color: string; bg: string }> = {
  enacted:      { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  to_president: { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  advancing:    { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  committee:    { color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  introduced:   { color: 'var(--text-3)', bg: 'var(--neutral-bg)' },
  failed:       { color: 'var(--reject)', bg: 'var(--neutral-bg)' },
};

/* The bill with a published review; its tile routes there instead of congress.gov. */
export const REVIEWED_BILLS: Record<string, string> = { '119-hr-6644': 'hr6644-119' };

function fromStatic(): TrackerBill[] {
  return CONGRESS_BILLS.map((b) => ({
    bill_id: '',
    ref: b.ref,
    congress: b.congress,
    chamber: b.chamber,
    title: b.title,
    status_key: b.statusKey,
    status_text: b.status,
    updated: null,
    introduced: null,
    category: b.category,
    sponsor: b.sponsor,
    summary: b.summary,
    congress_gov_url: 'https://www.congress.gov',
  }));
}

export interface BillCandidate {
  congress: number; bill_type: string; bill_number: number;
  ref: string; title: string; chamber: string; sponsor: string | null;
  introduced: string | null; latest_action_date: string | null;
  latest_action_text: string | null; status_key: string;
  tags: string[]; watch: boolean;
}

export interface DiscoverResult {
  days: number; listed: number; detail_calls: number; candidates: BillCandidate[];
}

export interface RefreshResult {
  bills: number; refreshed: number; texts_pulled: string[]; calls: number;
}

export interface AdminStats {
  tracked: { bills: number; with_text: number };
  untracked: { bills: number; with_text: number };
  last_refresh_at: string | null;
  last_discovery_at: string | null;
}

@Injectable({ providedIn: 'root' })
export class LegislationService {
  readonly bills = signal<TrackerBill[]>([]);
  readonly live = signal(false);

  constructor(private http: HttpClient) {
    this.reload();
  }

  reload(): void {
    this.http.get<TrackerBill[]>(`${API_BASE}/legislation`).subscribe({
      next: (rows) => {
        if (rows?.length) {
          this.bills.set(rows);
          this.live.set(true);
        } else {
          this.bills.set(fromStatic());
        }
      },
      error: () => this.bills.set(fromStatic()),
    });
  }

  // --- admin API ------------------------------------------------------------

  stats() {
    return this.http.get<AdminStats>(`${API_BASE}/admin/stats`);
  }

  refresh() {
    return this.http.post<RefreshResult>(`${API_BASE}/admin/refresh`, {});
  }

  discover(days: number) {
    return this.http.post<DiscoverResult>(`${API_BASE}/admin/discover?days=${days}`, {});
  }

  addBill(c: BillCandidate, tracked: boolean) {
    return this.http.post<{ bill_id: string; tracking_status: string }>(`${API_BASE}/admin/bills`, {
      congress: c.congress, bill_type: c.bill_type, bill_number: c.bill_number, tracked,
    });
  }

  refreshBill(billId: string) {
    return this.http.post<{ bill_id: string; texts_pulled: boolean; has_summary: boolean }>(
      `${API_BASE}/admin/bills/${billId}/refresh`, {},
    );
  }

  setTracking(billId: string, tracked: boolean) {
    return this.http.post<{ bill_id: string; tracking_status: string; texts_pulled: boolean }>(
      `${API_BASE}/admin/bills/${billId}/tracking`, { tracked },
    );
  }

  search(q: string, tracking: 'tracked' | 'untracked' | 'all') {
    const params = new URLSearchParams({ view: 'admin', tracking, limit: '100' });
    if (q.trim()) params.set('q', q.trim());
    return this.http.get<TrackerBill[]>(`${API_BASE}/legislation?${params}`);
  }

  setDisplay(billId: string, displayed: boolean) {
    return this.http.post<{ bill_id: string; display_date: string | null }>(
      `${API_BASE}/admin/bills/${billId}/display`, { displayed },
    );
  }

  setPin(billId: string, pinned: boolean) {
    return this.http.post<{ bill_id: string; pinned: boolean }>(
      `${API_BASE}/admin/bills/${billId}/pin`, { pinned },
    );
  }
}
