import { Component, ElementRef, ViewChild, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AI_CAPABILITIES, ChatMessage, INITIAL_MESSAGES, aiReply } from '../../core/site.data';
import { AssistantDocContext, AssistantService } from '../../core/assistant.service';

/* The Research Assistant. Two modes:
   - Document-scoped (?type=…&id=…): the full text of that bill/study/matter
     is in Gemini's context; answers are constrained to the document.
   - General (no params): the original corpus demo with canned replies. */

@Component({
  selector: 'app-assistant',
  imports: [RouterLink],
  templateUrl: './assistant.html',
})
export class AssistantPage {
  private svc = inject(AssistantService);
  private route = inject(ActivatedRoute);

  readonly capabilities = AI_CAPABILITIES;
  readonly messages = signal<ChatMessage[]>([...INITIAL_MESSAGES]);
  readonly typing = signal(false);
  readonly doc = signal<AssistantDocContext | null>(null);
  readonly docError = signal('');
  readonly chatError = signal('');

  @ViewChild('chatBox') chatBox?: ElementRef<HTMLDivElement>;
  @ViewChild('input') input?: ElementRef<HTMLTextAreaElement>;

  constructor() {
    this.route.queryParamMap.subscribe((p) => {
      const type = p.get('type');
      const id = p.get('id');
      this.doc.set(null);
      this.docError.set('');
      this.chatError.set('');
      if (type && id) {
        this.messages.set([]);
        this.svc.getContext(type, id).subscribe({
          next: (ctx) => this.doc.set(ctx),
          error: () => this.docError.set('Could not load that document — showing the general assistant instead.'),
        });
      } else {
        this.messages.set([...INITIAL_MESSAGES]);
      }
    });
  }

  /** First ~10 words of the document title, with an ellipsis. */
  shortTitle(title: string): string {
    const words = title.split(/\s+/);
    return words.length <= 10 ? title : words.slice(0, 10).join(' ') + '…';
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

  get prompts() {
    return this.doc() ? this.docPrompts : this.generalPrompts;
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
      this.svc.chat(doc.source_type, doc.source_key, history).subscribe({
        next: (r) => {
          this.typing.set(false);
          const note = r.document_truncated
            ? '\n\n(Note: this document was truncated to fit the context limit.)' : '';
          this.messages.update((m) => [...m, { role: 'ai', text: r.text + note }]);
          this.scrollChat();
        },
        error: (e) => {
          this.typing.set(false);
          this.chatError.set(e?.error?.detail || 'The assistant could not answer — is the API running?');
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

  private scrollChat(): void {
    requestAnimationFrame(() => {
      const el = this.chatBox?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }
}
