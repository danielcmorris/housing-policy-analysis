import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DbStudy, StudiesService } from '../../core/studies.service';
import { STUDY_STATUS } from '../../core/studies.data';

@Component({
  selector: 'app-admin-studies',
  imports: [RouterLink],
  templateUrl: './admin-studies.html',
})
export class AdminStudiesPage {
  readonly svc = inject(StudiesService);

  readonly rows = signal<DbStudy[]>([]);
  readonly saving = signal(false);
  readonly result = signal('');
  readonly error = signal('');
  readonly studyStatus = STUDY_STATUS;

  readonly statuses = ['Submitted', 'In Review', 'Awaiting Response', 'Peer Reviewed'];
  readonly categories = ['Rent Control', 'Building Policies', 'Regulation Reductions',
    'Filtering', 'Supply-Side Economics', 'Gentrification', 'Costs'];

  constructor() {
    this.load();
  }

  load(): void {
    this.svc.adminList('').subscribe({
      next: (rows) => this.rows.set(rows),
      error: () => this.error.set('Studies API is not reachable.'),
    });
  }

  submit(form: HTMLFormElement): void {
    if (this.saving()) return;
    const data = new FormData(form);
    if (!(data.get('ref') as string)?.trim() || !(data.get('title') as string)?.trim()) {
      this.error.set('Reference and title are required.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.result.set('');
    this.svc.add(data).subscribe({
      next: (r) => {
        this.saving.set(false);
        this.result.set(`Added ${r.ref}` +
          (r.pdf_stored ? ' with PDF' : '') +
          (r.text_stored ? ' and document text' : '') +
          (r.displayed ? ' — now public.' : ' — not displayed yet.'));
        form.reset();
        this.load();
        this.svc.reload();
      },
      error: (e) => {
        this.saving.set(false);
        this.error.set(e?.error?.detail || 'Could not save the document.');
      },
    });
  }

  pdfUrl(ref: string): string {
    return this.svc.pdfUrl(ref);
  }
}
