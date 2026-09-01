import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { openPreviewWindow, writeMarkdownPreview } from '../../core/markdown-preview';
import { AdminStudyDetail, StudiesService } from '../../core/studies.service';
import { STUDY_CATEGORIES, STUDY_STATUSES, STUDY_STATUS } from '../../core/studies.data';

/* Edit one study: every property, the stored document text, a replacement
   PDF, and the document's vector data (re-chunk + embedding pass). */
@Component({
  selector: 'app-admin-study-edit',
  imports: [RouterLink],
  templateUrl: './admin-study-edit.html',
})
export class AdminStudyEditPage {
  readonly svc = inject(StudiesService);
  private route = inject(ActivatedRoute);

  readonly ref = signal('');
  readonly detail = signal<AdminStudyDetail | null>(null);
  readonly notFound = signal(false);

  readonly saving = signal(false);
  readonly embedding = signal(false);
  readonly parsing = signal(false);
  readonly converting = signal(false);
  /** "You need a PDF first" popover on the Parse PDF button. */
  readonly noPdfDialog = signal(false);
  readonly result = signal('');
  readonly error = signal('');
  /** The user has edited the document text (or picked a text file), so the
      save must replace the stored text and re-chunk. */
  readonly textDirty = signal(false);

  readonly statuses = STUDY_STATUSES;
  readonly categories = STUDY_CATEGORIES;
  readonly studyStatus = STUDY_STATUS;

  constructor() {
    this.route.paramMap.subscribe((p) => {
      this.ref.set(p.get('ref') ?? '');
      this.load();
    });
  }

  load(): void {
    if (!this.ref()) return;
    this.svc.adminGet(this.ref()).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.notFound.set(false);
        this.textDirty.set(false);
      },
      error: (e) => {
        if (e?.status === 404) this.notFound.set(true);
        else this.error.set('Studies API is not reachable.');
      },
    });
  }

  pdfUrl(): string {
    return this.svc.pdfUrl(this.ref());
  }

  keyFindingsText(): string {
    return (this.detail()?.study.key_findings ?? []).join('\n');
  }

  private buildForm(form: HTMLFormElement): FormData | null {
    const data = new FormData(form);
    if (!(data.get('title') as string)?.trim()) {
      this.error.set('Title is required.');
      return null;
    }
    data.set('ref', this.ref());
    if (this.textDirty()) data.set('replaceText', 'true');
    return data;
  }

  save(form: HTMLFormElement, onSaved?: () => void): void {
    if (this.saving() || this.embedding()) return;
    const data = this.buildForm(form);
    if (!data) return;
    this.saving.set(true);
    this.error.set('');
    this.result.set('');
    this.svc.update(this.ref(), data).subscribe({
      next: (r) => {
        this.saving.set(false);
        this.result.set(`Saved ${r.ref}` +
          (r.pdf_replaced ? ' — PDF replaced' : '') +
          (r.rechunked ? ' — text re-chunked, embeddings pending.' : '.'));
        this.svc.reload();
        if (onSaved) onSaved(); else this.load();
      },
      error: (e) => {
        this.saving.set(false);
        this.error.set(e?.error?.detail || 'Could not save the document.');
      },
    });
  }

  /** Extract the stored PDF's text into the editor (nothing saved until the
      admin saves). Without a PDF on file, shows the "upload one first" popover. */
  parsePdf(): void {
    const d = this.detail();
    if (!d || this.parsing() || this.converting()) return;
    if (!d.study.has_pdf) {
      this.noPdfDialog.set(true);
      return;
    }
    this.parsing.set(true);
    this.error.set('');
    this.result.set('');
    this.svc.parsePdf(this.ref()).subscribe({
      next: (r) => {
        this.parsing.set(false);
        this.detail.update((cur) => cur ? { ...cur, text_content: r.text } : cur);
        this.textDirty.set(true);
        this.result.set(`Parsed ${r.pages} page${r.pages === 1 ? '' : 's'} `
          + `(${r.characters.toLocaleString()} characters) from the PDF — review the text below, then save.`);
      },
      error: (e) => {
        this.parsing.set(false);
        this.error.set(e?.error?.detail || 'Could not parse the PDF.');
      },
    });
  }

  /** Render the editor's current text as markdown in a new window. */
  preview(form: HTMLFormElement): void {
    const text = (form.elements.namedItem('textContent') as HTMLTextAreaElement | null)?.value ?? '';
    if (!text.trim()) {
      this.error.set('There is no document text to preview.');
      return;
    }
    const w = openPreviewWindow();
    if (!w) {
      this.error.set('The browser blocked the preview window — allow pop-ups for this site.');
      return;
    }
    writeMarkdownPreview(w, `Preview — ${this.ref()}`,
      `Markdown preview — ${this.ref()} (unsaved editor content)`, text);
  }

  /** Convert the stored PDF to Markdown via Gemini flash-lite and place it in
      the editor (nothing saved until the admin saves). Costs ~2¢/document
      worst case under the configured caps; usage lands in the ai_usage ledger. */
  convertMarkdown(): void {
    const d = this.detail();
    if (!d || this.converting() || this.parsing()) return;
    if (!d.study.has_pdf) {
      this.noPdfDialog.set(true);
      return;
    }
    this.converting.set(true);
    this.error.set('');
    this.result.set('');
    this.svc.convertMarkdown(this.ref()).subscribe({
      next: (r) => {
        this.converting.set(false);
        this.detail.update((cur) => cur ? { ...cur, text_content: r.text } : cur);
        this.textDirty.set(true);
        this.result.set(`Converted ${r.pages} page${r.pages === 1 ? '' : 's'} to Markdown `
          + `(${r.input_tokens.toLocaleString()} in / ${r.output_tokens.toLocaleString()} out tokens, `
          + `${r.model}) — review the text below, then save.`);
      },
      error: (e) => {
        this.converting.set(false);
        this.error.set(e?.error?.detail || 'Markdown conversion failed.');
      },
    });
  }

  /** Save, then run the embedding pass for this document (calls Vertex AI). */
  updateVectors(form: HTMLFormElement): void {
    this.save(form, () => {
      this.embedding.set(true);
      this.svc.embed(this.ref()).subscribe({
        next: (r) => {
          this.embedding.set(false);
          this.result.set(r.chunks_embedded > 0
            ? `Embedded ${r.chunks_embedded} chunk${r.chunks_embedded === 1 ? '' : 's'} `
              + `(${r.input_tokens} tokens, ${r.model}); ${r.chunks_still_pending} still pending overall.`
            : 'Vector data is already up to date — no chunks were pending for this document.');
          this.load();
        },
        error: (e) => {
          this.embedding.set(false);
          this.error.set(e?.error?.detail || 'The embedding pass failed.');
          this.load();
        },
      });
    });
  }
}
