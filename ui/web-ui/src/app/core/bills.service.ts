import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE } from './legislation.service';

/* Bill-review data per prototype/data/bill-review.schema.json.
   Served by GET /reviews from the law-retrieval API (stored in Postgres,
   with live legislative status merged in). Falls back to the static
   public/data/bills.json seed when the API is unreachable. */

export interface Vote { yea: number; nay: number; }
export interface LegStage { stage: string; state: 'complete' | 'in_progress' | 'pending'; date?: string; vote?: Vote; }
export interface BillMeta {
  billNumber: string; sourceUrl: string; status: string; overallConfidence: string;
  legislativeStatus: LegStage[]; precedentRefs: string[];
}
export interface BillContent {
  category: string; title: string; excerpt: string; summary: string; methodNote: string;
  congressLine: string; introducedLabel: string; sponsor: string;
  provisions: { tag: string; title: string; body: string }[];
  precedents: { ref: string; match: string; title: string; finding: string; confidence: string }[];
  effects: { label: string; body: string }[];
  projections: { provision: string; effect: string; horizon: string; confidence: string }[];
  outlook: { headline: string; body: string; confidence: string };
  aiNote: string;
  facts: { k: string; v: string }[];
  reviews: { initials: string; name: string; affil: string; date: string; score: string; recommendation: string; text: string }[];
}
export interface Bill { id: string; schemaVersion: string; meta: BillMeta; content: Record<string, BillContent>; }
export interface BillStore { version: string; defaultLocale: string; featuredBillId: string; bills: Record<string, Bill>; }

@Injectable({ providedIn: 'root' })
export class BillsService {
  readonly store = signal<BillStore | null>(null);
  readonly featuredBillId = signal<string>('hr6644-119');

  constructor(private http: HttpClient) {
    this.http.get<BillStore>(`${API_BASE}/reviews`).subscribe({
      next: (d) => {
        if (d && d.bills && Object.keys(d.bills).length) {
          this.apply(d);
        } else {
          this.loadFallback();
        }
      },
      error: () => this.loadFallback(),
    });
  }

  private loadFallback(): void {
    this.http.get<BillStore>('data/bills.json').subscribe({
      next: (d) => { if (d && d.bills) this.apply(d); },
      error: () => {},
    });
  }

  private apply(d: BillStore): void {
    this.store.set(d);
    if (d.featuredBillId) this.featuredBillId.set(d.featuredBillId);
  }

  bill(id: string): Bill | null {
    const s = this.store();
    if (!s) return null;
    return s.bills[id] ?? s.bills[this.featuredBillId()] ?? null;
  }

  content(id: string, locale = 'en'): { meta: BillMeta; c: BillContent } | null {
    const b = this.bill(id);
    if (!b) return null;
    const c = b.content[locale] ?? b.content['en'];
    return { meta: b.meta, c };
  }
}
