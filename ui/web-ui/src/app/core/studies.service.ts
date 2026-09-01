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
  display_date: string | null;
  pinned: boolean;
}

/** Admin edit view: the row plus the stored document text and the
    document's chunk/embedding counts from the RAG registry. */
export interface AdminStudyDetail {
  study: DbStudy;
  text_content: string | null;
  chunks: number;
  chunks_pending: number;
}

export interface StudySaveResult {
  ref: string;
  pdf_replaced: boolean;
  text_replaced: boolean;
  rechunked: boolean;
  displayed: boolean;
}

export interface EmbedResult {
  model: string;
  chunks_embedded: number;
  input_tokens: number;
  chunks_still_pending: number;
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

  adminGet(ref: string) {
    return this.http.get<AdminStudyDetail>(`${API_BASE}/admin/studies/${ref}`);
  }

  update(ref: string, form: FormData) {
    return this.http.put<StudySaveResult>(`${API_BASE}/admin/studies/${ref}`, form);
  }

  setDisplay(ref: string, displayed: boolean) {
    return this.http.post<{ ref: string; display_date: string | null; displayed: boolean }>(
      `${API_BASE}/admin/studies/${ref}/display`, { displayed },
    );
  }

  setPin(ref: string, pinned: boolean) {
    return this.http.post<{ ref: string; pinned: boolean }>(
      `${API_BASE}/admin/studies/${ref}/pin`, { pinned },
    );
  }

  /** Extract the stored PDF's text with PdfPig (local, nothing is saved). */
  parsePdf(ref: string) {
    return this.http.post<{ ref: string; pages: number; characters: number; text: string }>(
      `${API_BASE}/admin/studies/${ref}/parse-pdf`, {},
    );
  }

  /** Convert the stored PDF to Markdown via Gemini flash-lite (calls Vertex
      AI; page/output caps bound the cost, usage is ledgered; nothing saved). */
  convertMarkdown(ref: string) {
    return this.http.post<{ ref: string; pages: number; characters: number;
                            input_tokens: number; output_tokens: number;
                            model: string; text: string }>(
      `${API_BASE}/admin/studies/${ref}/convert-markdown`, {},
    );
  }

  /** Embed this study's pending chunks (calls Vertex AI via the API). */
  embed(ref: string) {
    return this.http.post<EmbedResult>(`${API_BASE}/admin/registry/embed`,
      { source_type: 'study', source_key: ref });
  }
}
