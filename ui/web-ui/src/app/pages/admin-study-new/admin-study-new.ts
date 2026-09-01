import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { StudiesService } from '../../core/studies.service';
import { STUDY_CATEGORIES, STUDY_STATUSES } from '../../core/studies.data';

@Component({
  selector: 'app-admin-study-new',
  imports: [RouterLink],
  templateUrl: './admin-study-new.html',
})
export class AdminStudyNewPage {
  readonly svc = inject(StudiesService);
  private router = inject(Router);

  readonly saving = signal(false);
  readonly result = signal('');
  readonly error = signal('');

  readonly statuses = STUDY_STATUSES;
  readonly categories = STUDY_CATEGORIES;

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
        this.svc.reload();
        this.router.navigate(['/admin/studies', r.ref]);
      },
      error: (e) => {
        this.saving.set(false);
        this.error.set(e?.error?.detail || 'Could not save the document.');
      },
    });
  }
}
