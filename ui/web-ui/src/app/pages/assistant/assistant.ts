import { Component, ElementRef, ViewChild, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AI_CAPABILITIES, ChatMessage, INITIAL_MESSAGES, aiReply } from '../../core/site.data';

@Component({
  selector: 'app-assistant',
  imports: [RouterLink],
  templateUrl: './assistant.html',
})
export class AssistantPage {
  readonly capabilities = AI_CAPABILITIES;
  readonly messages = signal<ChatMessage[]>([...INITIAL_MESSAGES]);
  readonly typing = signal(false);

  @ViewChild('chatBox') chatBox?: ElementRef<HTMLDivElement>;
  @ViewChild('input') input?: ElementRef<HTMLTextAreaElement>;

  readonly prompts = [
    { label: 'Compare rent-control vs. lot-size evidence', text: 'Compare rent-control findings with the Houston lot-size study.' },
    { label: 'Generate peer-review questions for the Filtering study', text: 'Generate peer-review questions for the Filtering study.' },
    { label: 'What does the evidence say about lot sizes and costs?', text: 'What does the evidence say about minimum lot sizes and costs?' },
  ];

  send(text: string): void {
    const v = (text || '').trim();
    if (!v) return;
    this.messages.update((m) => [...m, { role: 'user', text: v }]);
    this.typing.set(true);
    this.scrollChat();
    setTimeout(() => {
      this.typing.set(false);
      this.messages.update((m) => [...m, aiReply(v)]);
      this.scrollChat();
    }, 1100);
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
