import { Component, ElementRef, ViewChild, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { marked } from 'marked';
import { AI_CAPABILITIES, ChatMessage, INITIAL_MESSAGES, aiReply } from '../../core/site.data';
import {
  AssistantDocContext, AssistantRelatedDoc, AssistantService,
} from '../../core/assistant.service';
import { SearchService } from '../../core/search.service';

marked.use({ gfm: true, breaks: true });

/* The Research Assistant. Two modes:
   - Document-scoped (?type=…&id=…): the full text of that bill/study/matter
     is in Gemini's context; answers are constrained to the document. Up to
     4 comparison documents can ride along (full text while the token budget
     allows, question-relevant excerpts beyond that).
   - General (no params): the original corpus demo with canned replies. */

const MAX_COMPARE = 4;

@Component({
  selector: 'app-assistant',
  imports: [RouterLink],
  templateUrl: './assistant.html',
})
export class AssistantPage {
  private svc = inject(AssistantService);
  private search = inject(SearchService);
  private route = inject(ActivatedRoute);

  readonly capabilities = AI_CAPABILITIES;
  readonly messages = signal<ChatMessage[]>([...INITIAL_MESSAGES]);
  readonly typing = signal(false);
  readonly doc = signal<AssistantDocContext | null>(null);
  readonly docError = signal('');
  readonly chatError = signal('');

  // Comparison rail (session-only, scoped mode)
  readonly compare = signal<AssistantRelatedDoc[]>([]);
  readonly related = signal<AssistantRelatedDoc[]>([]);
  /** 'type/key' -> how the doc rode in the LAST answer ('full' | 'excerpts'). */
  readonly compareModes = signal<Record<string, string>>({});
  readonly pickerResults = signal<AssistantRelatedDoc[]>([]);
  readonly pickerBusy = signal(false);
  private pickerTimer: ReturnType<typeof setTimeout> | null = null;

  readonly compareTokens = computed(() =>
    this.compare().reduce((sum, c) => sum + (c.token_estimate || 0), 0));
  readonly anyExcerpts = computed(() =>
    Object.values(this.compareModes()).includes('excerpts'));

  @ViewChild('chatBox') chatBox?: ElementRef<HTMLDivElement>;
  @ViewChild('input') input?: ElementRef<HTMLTextAreaElement>;
  @ViewChild('picker') picker?: ElementRef<HTMLInputElement>;

  constructor() {
    this.route.queryParamMap.subscribe((p) => {
      const type = p.get('type');
      const id = p.get('id');
      this.doc.set(null);
      this.docError.set('');
      this.chatError.set('');
      this.compare.set([]);
      this.related.set([]);
      this.compareModes.set({});
      this.pickerResults.set([]);
      if (type && id) {
        this.messages.set([]);
        this.svc.getContext(type, id).subscribe({
          next: (ctx) => {
            this.doc.set(ctx);
            this.svc.getRelated(type, id).subscribe({
              next: (r) => this.related.set(r.related),
              error: () => this.related.set([]),
            });
          },
          error: () => this.docError.set('Could not load that document — showing the general assistant instead.'),
        });
      } else {
        this.messages.set([...INITIAL_MESSAGES]);
      }
    });
  }

  /** Assistant replies arrive as markdown; render them to HTML (Angular
      sanitizes [innerHTML] bindings, so scripts/handlers are stripped). */
  renderMd(text: string): string {
    return marked.parse(text, { async: false }) as string;
  }

  /** First ~10 words of the document title, with an ellipsis. */
  shortTitle(title: string | null, words = 10): string {
    const parts = (title || '').split(/\s+/);
    return parts.length <= words ? (title || '') : parts.slice(0, words).join(' ') + '…';
  }

  key(d: { source_type: string; source_key: string }): string {
    return `${d.source_type}/${d.source_key}`;
  }

  relationLabel(d: AssistantRelatedDoc): string {
    if (d.relation === 'similar')
      return d.similarity != null ? `${Math.round(d.similarity * 100)}% similar` : 'similar';
    return d.relation;
  }

  isSelected(d: { source_type: string; source_key: string }): boolean {
    const primary = this.doc();
    if (primary && primary.source_type === d.source_type && primary.source_key === d.source_key) return true;
    return this.compare().some((c) => c.source_type === d.source_type && c.source_key === d.source_key);
  }

  addCompare(d: AssistantRelatedDoc): void {
    if (this.compare().length >= MAX_COMPARE || this.isSelected(d)) return;
    this.compare.update((c) => [...c, d]);
  }

  removeCompare(d: AssistantRelatedDoc): void {
    this.compare.update((c) => c.filter(
      (x) => !(x.source_type === d.source_type && x.source_key === d.source_key)));
    this.compareModes.update((m) => {
      const next = { ...m };
      delete next[this.key(d)];
      return next;
    });
  }

  onPickerInput(): void {
    const q = (this.picker?.nativeElement.value || '').trim();
    if (this.pickerTimer) clearTimeout(this.pickerTimer);
    if (q.length < 3) {
      this.pickerResults.set([]);
      return;
    }
    this.pickerTimer = setTimeout(() => {
      this.pickerBusy.set(true);
      this.search.search(q).subscribe({
        next: (r) => {
          this.pickerBusy.set(false);
          this.pickerResults.set(r.documents.slice(0, 6).map((d) => ({
            source_type: d.source_type,
            source_key: d.source_key,
            title: d.title,
            jurisdiction: d.jurisdiction,
            doc_year: d.doc_year,
            relation: 'search',
            similarity: null,
            token_estimate: 0,
          })));
        },
        error: () => {
          this.pickerBusy.set(false);
          this.pickerResults.set([]);
        },
      });
    }, 350);
  }

  readonly generalPrompts = [
    { label: 'Compare rent-control vs. lot-size evidence', text: 'Compare rent-control findings with the Houston lot-size study.' },
    { label: 'Generate peer-review questions for the Filtering study', text: 'Generate peer-review questions for the Filtering study.' },
    { label: 'What does the evidence say about lot sizes and costs?', text: 'What does the evidence say about minimum lot sizes and costs?' },
  ];

  readonly docPrompts = [
    { label: 'Summarize the key provisions', text: 'Summarize the key provisions of this document.' },
    { label: 'Who does it affect, and how?', text: 'Who is affected by this document, and how?' },
    { label: 'What deadlines, funding, or programs does it create?', text: 'What deadlines, funding amounts, or new programs does this document establish?' },
  ];

  readonly comparePrompts = [
    { label: 'How do these documents differ?', text: 'How does this document differ from the comparison documents? Ground the comparison in the texts.' },
    { label: 'Where do they agree?', text: 'Where do this document and the comparison documents agree or reinforce each other?' },
  ];

  get prompts() {
    if (!this.doc()) return this.generalPrompts;
    return this.compare().length ? [...this.comparePrompts, ...this.docPrompts.slice(0, 1)] : this.docPrompts;
  }

  send(text: string): void {
    const v = (text || '').trim();
    if (!v || this.typing()) return;
    this.chatError.set('');
    this.messages.update((m) => [...m, { role: 'user', text: v }]);
    this.typing.set(true);
    this.scrollChat();

    const doc = this.doc();
    if (doc) {
      const history = this.messages().map((m) => ({ role: m.role, text: m.text }));
      const compare = this.compare().map((c) => ({
        source_type: c.source_type, source_key: c.source_key,
      }));
      this.svc.chat(doc.source_type, doc.source_key, history, compare).subscribe({
        next: (r) => {
          this.typing.set(false);
          const note = r.document_truncated
            ? '\n\n(Note: this document was truncated to fit the context limit.)' : '';
          this.messages.update((m) => [...m, { role: 'ai', text: r.text + note }]);
          const modes: Record<string, string> = {};
          for (const cd of r.context_docs || []) modes[this.key(cd)] = cd.mode;
          this.compareModes.set(modes);
          this.scrollChat();
        },
        error: (e) => {
          this.typing.set(false);
          this.chatError.set(e?.error?.detail || 'The assistant could not answer — is the API running?');
          this.scrollChat();
        },
      });
    } else {
      setTimeout(() => {
        this.typing.set(false);
        this.messages.update((m) => [...m, aiReply(v)]);
        this.scrollChat();
      }, 1100);
    }
  }

  sendTyped(): void {
    const el = this.input?.nativeElement;
    if (!el) return;
    const v = el.value;
    el.value = '';
    this.send(v);
  }

  onKey(e: KeyboardEvent): void {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      this.sendTyped();
    }
  }

  /** Pin the chat to the bottom. Runs several passes because rendered
      markdown (and font loading) changes the height after the first frame. */
  private scrollChat(): void {
    const scroll = () => {
      const el = this.chatBox?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    };
    requestAnimationFrame(scroll);
    setTimeout(scroll, 80);
    setTimeout(scroll, 300);
  }
}
