import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE } from './legislation.service';

/* Document-scoped assistant: the full text of one bill / study / city matter
   rides in Gemini's context window with the running conversation — plus up
   to four comparison documents, full text while the budget allows and
   question-relevant excerpts beyond that. */

export interface AssistantDocContext {
  source_type: string;
  source_key: string;
  title: string;
  kind: string;                       // 'bill' | 'study' | 'city matter'
  link_kind: 'internal' | 'external';
  link_href: string;
  has_text: boolean;
  token_estimate: number;
}

/** A candidate or selected comparison document. */
export interface AssistantRelatedDoc {
  source_type: string;
  source_key: string;
  title: string | null;
  jurisdiction: string | null;
  doc_year: number | null;
  relation: string;                   // 'precedent' | 'related' | … | 'similar'
  similarity: number | null;          // null for curated relations
  token_estimate: number;
}

/** What actually rode along with the last answer, per comparison doc. */
export interface AssistantContextDoc {
  source_type: string;
  source_key: string;
  title: string;
  mode: 'full' | 'excerpts';
  tokens: number;
}

export interface AssistantChatResult {
  text: string;
  model: string;
  input_tokens: number;
  output_tokens: number;
  document_truncated: boolean;
  context_docs: AssistantContextDoc[];
}

@Injectable({ providedIn: 'root' })
export class AssistantService {
  constructor(private http: HttpClient) {}

  getContext(sourceType: string, sourceKey: string) {
    const params = new URLSearchParams({ source_type: sourceType, source_key: sourceKey });
    return this.http.get<AssistantDocContext>(`${API_BASE}/assistant/context?${params}`);
  }

  /** The document's stored text (published documents only), for the preview window. */
  getText(sourceType: string, sourceKey: string) {
    const params = new URLSearchParams({ source_type: sourceType, source_key: sourceKey });
    return this.http.get<{ title: string; text: string }>(`${API_BASE}/assistant/text?${params}`);
  }

  getRelated(sourceType: string, sourceKey: string, topK = 5) {
    const params = new URLSearchParams({
      source_type: sourceType, source_key: sourceKey, top_k: String(topK),
    });
    return this.http.get<{ related: AssistantRelatedDoc[] }>(`${API_BASE}/assistant/related?${params}`);
  }

  chat(
    sourceType: string,
    sourceKey: string,
    messages: { role: string; text: string }[],
    compare: { source_type: string; source_key: string }[] = [],
  ) {
    return this.http.post<AssistantChatResult>(`${API_BASE}/assistant/chat`, {
      source_type: sourceType,
      source_key: sourceKey,
      messages,
      compare,
    });
  }
}
