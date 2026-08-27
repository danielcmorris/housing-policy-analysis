import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE } from './legislation.service';

/* Vector search over the unified document registry (POST /api/search).
   Results here are document-level: best-matching chunk per document, with
   tags and a link target resolved by the API. */

export interface SearchDoc {
  document_id: number;
  source_type: 'federal_bill' | 'city_matter' | 'study' | string;
  source_key: string;
  title: string | null;
  jurisdiction: string | null;
  doc_year: number | null;
  tags: string[];
  snippet: string;
  score: number;
  chunk_matches: number;
  link: { kind: 'internal' | 'external'; href: string };
}

export interface SearchResponse {
  mode: 'vector' | 'keyword';
  query: string;
  documents: SearchDoc[];
}

/** Similarity floor for "found with decent certainty" (vector mode only). */
export const MIN_SCORE = 0.45;

@Injectable({ providedIn: 'root' })
export class SearchService {
  constructor(private http: HttpClient) {}

  search(query: string) {
    return this.http.post<SearchResponse>(`${API_BASE}/search`, {
      query,
      top_k: 30,
      min_score: MIN_SCORE,
    });
  }
}
