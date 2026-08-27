import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExpertsService } from '../../core/experts.service';
import { StudiesService } from '../../core/studies.service';

interface AdminExpertRow {
  slug: string; full_name: string; title: string | null; affiliation: string | null;
  category: string | null; focus: string | null; bio: string | null;
  credentials: string | null; linkedin_url: string | null; profile_url: string | null;
  scholar_url: string | null; website_url: string | null; image_url: string | null;
  location: string | null; conflicts: string | null; active: boolean;
  study_review_count: number; bill_review_count: number;
}

@Component({
  selector: 'app-admin-experts',
  imports: [RouterLink],
  templateUrl: './admin-experts.html',
})
export class AdminExpertsPage {
  readonly svc = inject(ExpertsService);
  readonly studiesSvc = inject(StudiesService);

  readonly rows = signal<AdminExpertRow[]>([]);
  readonly query = signal('');
  readonly editing = signal<AdminExpertRow | null>(null);
  readonly saving = signal(false);
  readonly message = signal('');
  readonly error = signal('');

  readonly categories = ['Academic', 'Think tank', 'Research center',
    'Non-profit research', 'Non-profit research/advocacy', 'Other'];

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.load();
  }

  onQuery(v: string): void {
    this.query.set(v);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 250);
  }

  load(): void {
    this.svc.adminList(this.query()).subscribe({
      next: (rows) => this.rows.set(rows as unknown as AdminExpertRow[]),
      error: () => this.error.set('Experts API is not reachable.'),
    });
  }

  edit(row: AdminExpertRow | null): void {
    this.editing.set(row);
    this.message.set('');
    this.error.set('');
  }

  save(form: HTMLFormElement): void {
    if (this.saving()) return;
    const data = new FormData(form);
    const body: Record<string, unknown> = {};
    for (const [k, v] of data.entries()) body[k] = (v as string).trim() || null;
    body['active'] = data.get('active') === 'true';
    if (!body['full_name']) { this.error.set('Full name is required.'); return; }
    this.saving.set(true);
    this.error.set('');
    this.svc.upsert(body).subscribe({
      next: (r) => {
        this.saving.set(false);
        this.message.set(`Saved ${r.slug}.`);
        this.editing.set(null);
        form.reset();
        this.load();
        this.svc.reload();
      },
      error: (e) => {
        this.saving.set(false);
        this.error.set(e?.error?.detail || 'Could not save.');
      },
    });
  }

  recordReview(form: HTMLFormElement): void {
    if (this.saving()) return;
    const data = new FormData(form);
    const type = data.get('review_type') as string;
    const body: Record<string, unknown> = {
      expert_slug: (data.get('expert_slug') as string)?.trim(),
      recommendation: (data.get('recommendation') as string) || null,
      score: data.get('score') ? Number(data.get('score')) : null,
      review_text: (data.get('review_text') as string)?.trim() || null,
    };
    const target = (data.get('target') as string)?.trim();
    if (!body['expert_slug'] || !target) {
      this.error.set('Expert and target are required.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    const call = type === 'bill'
      ? this.svc.addBillReview({ ...body, review_id: target })
      : this.svc.addStudyReview({ ...body, study_ref: target });
    call.subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set(`Review recorded for ${body['expert_slug']} on ${target}.`);
        form.reset();
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        this.error.set(e?.error?.detail || 'Could not record the review.');
      },
    });
  }
}
