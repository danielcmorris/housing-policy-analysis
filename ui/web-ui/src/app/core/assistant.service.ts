import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE } from './legislation.service';

/* Document-scoped assistant: the full text of one bill / study / city matter
   rides in Gemini's context window with the running conversation. */

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

export interface AssistantChatResult {
  text: string;
  model: string;
  input_tokens: number;
  output_tokens: number;
  document_truncated: boolean;
}

@Injectable({ providedIn: 'root' })
export class AssistantService {
  constructor(private http: HttpClient) {}

  getContext(sourceType: string, sourceKey: string) {
    const params = new URLSearchParams({ source_type: sourceType, source_key: sourceKey });
    return this.http.get<AssistantDocContext>(`${API_BASE}/assistant/context?${params}`);
  }

  chat(sourceType: string, sourceKey: string, messages: { role: string; text: string }[]) {
    return this.http.post<AssistantChatResult>(`${API_BASE}/assistant/chat`, {
      source_type: sourceType,
      source_key: sourceKey,
      messages,
    });
  }
}
