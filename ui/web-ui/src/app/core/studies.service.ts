import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE } from './legislation.service';

/* Studies & policy proposals from the database (added manually via the admin
   page; the PDF lives on the API's disk for now). The static demo studies in
   studies.data.ts remain as fallback/demo content alongside these. */

export interface DbStudy {
  ref: string;
  doc_type: 'study' | 'proposal' | string;
  title: string;
  category: string | null;
  authors: string | null;
  year: number | null;
  pages: number | null;
  status: string;
  clarity: number | null;
  summary: string | null;
  key_findings: string[];
  methodology: string | null;
  has_text: boolean;
  has_pdf: boolean;
  displayed: boolean;
  pinned: boolean;
}

export interface StudyReviewPublic {
  expert_slug: string;
  name: string;
  affil: string | null;
  recommendation: string | null;
  score: number | null;
  text: string | null;
  reviewed_at: string | null;
}

export interface StudyDetail {
  study: DbStudy;
  reviews: StudyReviewPublic[];
}

@Injectable({ providedIn: 'root' })
export class StudiesService {
  readonly studies = signal<DbStudy[]>([]);
  readonly loaded = signal(false);

  constructor(private http: HttpClient) {
    this.reload();
  }

  reload(): void {
    this.http.get<DbStudy[]>(`${API_BASE}/studies`).subscribe({
      next: (rows) => {
        this.studies.set(rows ?? []);
        this.loaded.set(true);
      },
      error: () => this.loaded.set(true),
    });
  }

  get(ref: string) {
    return this.http.get<StudyDetail>(`${API_BASE}/studies/${ref}`);
  }

  adminList(q: string) {
    const params = new URLSearchParams({ view: 'admin' });
    if (q.trim()) params.set('q', q.trim());
    return this.http.get<DbStudy[]>(`${API_BASE}/studies?${params}`);
  }

  add(form: FormData) {
    return this.http.post<{ ref: string; pdf_stored: boolean; text_stored: boolean; displayed: boolean }>(
      `${API_BASE}/admin/studies`, form,
    );
  }

  pdfUrl(ref: string): string {
    return `${API_BASE}/studies/${ref}/pdf`;
  }
}
