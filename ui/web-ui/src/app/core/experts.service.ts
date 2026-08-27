import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE } from './legislation.service';

/* Experts / reviewers. Served from the database (GET /api/experts, seeded
   from the public roster); the static experts.json remains a fallback when
   the API is unreachable. Public payloads never include email or notes. */

export interface Expert {
  slug: string;
  name: string;
  title: string;
  affiliation: string;
  category: string;
  focus: string;
  bio?: string | null;
  credentials?: string | null;
  linkedin_url?: string | null;
  profile_url: string;
  scholar_url?: string | null;
  website_url?: string | null;
  image_url: string;
  location?: string | null;
  conflicts?: string | null;
  active?: boolean;
  study_review_count?: number;
  bill_review_count?: number;
}

export interface ExpertStudyReview {
  study_ref: string; study_title: string | null; recommendation: string | null;
  score: number | null; review_text: string | null; reviewed_at: string | null;
}

export interface ExpertBillReview {
  review_id: string; bill_id: string | null; bill_title: string | null;
  recommendation: string | null; score: number | null; review_text: string | null;
  reviewed_at: string | null;
}

export interface ExpertProfile {
  expert: ApiExpert;
  study_reviews: ExpertStudyReview[];
  bill_reviews: ExpertBillReview[];
}

interface ApiExpert {
  slug: string; full_name: string; title: string | null; affiliation: string | null;
  category: string | null; focus: string | null; bio: string | null;
  credentials: string | null; linkedin_url: string | null; profile_url: string | null;
  scholar_url: string | null; website_url: string | null; image_url: string | null;
  location: string | null; conflicts: string | null; active: boolean;
  study_review_count: number; bill_review_count: number;
}

export interface ExpertCatMeta { bucket: string; label: string; color: string; bg: string; avatar: string; }

const CAT_STYLES: Record<string, { color: string; bg: string; avatar: string }> = {
  'Academic':        { color: 'var(--accent)', bg: 'var(--accent-soft)', avatar: 'var(--accent-fill)' },
  'Think tank':      { color: 'var(--gold)',   bg: 'var(--surface-2)',   avatar: 'var(--gold)' },
  'Research center': { color: 'var(--text-2)', bg: 'var(--surface-2)',   avatar: 'var(--inverse)' },
  'Non-profit':      { color: 'var(--ok)',     bg: 'var(--ok-bg)',       avatar: 'var(--ok)' },
  'Other':           { color: 'var(--text-3)', bg: 'var(--surface-2)',   avatar: 'var(--inverse)' },
};

export function mapApiExpert(e: ApiExpert): Expert {
  return {
    slug: e.slug,
    name: e.full_name,
    title: e.title ?? '',
    affiliation: e.affiliation ?? '',
    category: e.category ?? 'Other',
    focus: e.focus ?? '',
    bio: e.bio,
    credentials: e.credentials,
    linkedin_url: e.linkedin_url,
    profile_url: e.profile_url ?? '',
    scholar_url: e.scholar_url,
    website_url: e.website_url,
    image_url: e.image_url ?? '',
    location: e.location,
    conflicts: e.conflicts,
    active: e.active,
    study_review_count: e.study_review_count,
    bill_review_count: e.bill_review_count,
  };
}

@Injectable({ providedIn: 'root' })
export class ExpertsService {
  readonly experts = signal<Expert[]>([]);
  readonly live = signal(false);

  constructor(private http: HttpClient) {
    this.reload();
  }

  reload(): void {
    this.http.get<ApiExpert[]>(`${API_BASE}/experts`).subscribe({
      next: (rows) => {
        if (rows?.length) {
          this.experts.set(rows.map(mapApiExpert));
          this.live.set(true);
        } else {
          this.loadFallback();
        }
      },
      error: () => this.loadFallback(),
    });
  }

  private loadFallback(): void {
    this.http.get<{ experts: { name: string; title: string; affiliation: string; category: string; focus: string; profile_url: string; image_url: string }[] }>('data/experts.json').subscribe({
      next: (d) => this.experts.set((d?.experts ?? []).map((e) => ({
        slug: e.name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, ''),
        name: e.name, title: e.title, affiliation: e.affiliation,
        category: e.category, focus: e.focus, profile_url: e.profile_url, image_url: e.image_url,
      }))),
      error: () => {},
    });
  }

  get(slug: string) {
    return this.http.get<ExpertProfile>(`${API_BASE}/experts/${slug}`);
  }

  adminList(q: string) {
    const params = new URLSearchParams({ include_inactive: 'true' });
    if (q.trim()) params.set('q', q.trim());
    return this.http.get<ApiExpert[]>(`${API_BASE}/experts?${params}`);
  }

  upsert(body: Record<string, unknown>) {
    return this.http.post<{ slug: string }>(`${API_BASE}/admin/experts`, body);
  }

  addStudyReview(body: Record<string, unknown>) {
    return this.http.post<{ recorded: boolean }>(`${API_BASE}/admin/study-reviews`, body);
  }

  addBillReview(body: Record<string, unknown>) {
    return this.http.post<{ recorded: boolean }>(`${API_BASE}/admin/bill-reviews`, body);
  }

  initials(name: string): string {
    const parts = (name || '').replace(/[^A-Za-z\s'-]/g, '').trim().split(/\s+/);
    if (!parts.length || !parts[0]) return '?';
    const first = parts[0][0] || '';
    const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
    return (first + last).toUpperCase();
  }

  catMeta(category: string): ExpertCatMeta {
    const bucket = (category || '').indexOf('Non-profit') === 0 ? 'Non-profit'
      : (category === 'Academic' ? 'Academic'
      : (category === 'Think tank' ? 'Think tank'
      : (category === 'Research center' ? 'Research center' : 'Other')));
    return { bucket, label: bucket, ...CAT_STYLES[bucket] };
  }
}
