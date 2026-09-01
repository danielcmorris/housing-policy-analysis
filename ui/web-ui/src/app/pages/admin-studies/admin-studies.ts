import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DbStudy, StudiesService } from '../../core/studies.service';
import { STUDY_STATUS, clarityColor } from '../../core/studies.data';

type TypeFilter = 'all' | 'study' | 'proposal';

/* Admin list of every study/proposal on file (the bills-manager pattern):
   search, expandable rows, display/pin toggles, and links to the edit page.
   Adding a new document lives on /admin/studies/new. */
@Component({
  selector: 'app-admin-studies',
  imports: [RouterLink, DatePipe],
  templateUrl: './admin-studies.html',
})
export class AdminStudiesPage {
  readonly svc = inject(StudiesService);

  readonly query = signal('');
  readonly typeFilter = signal<TypeFilter>('all');
  readonly rows = signal<DbStudy[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly expanded = signal<Set<string>>(new Set());

  readonly studyStatus = STUDY_STATUS;
  readonly clarityColor = clarityColor;
  readonly filters: [TypeFilter, string][] = [
    ['all', 'All'], ['study', 'Studies'], ['proposal', 'Proposals'],
  ];

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.load();
  }

  onQuery(value: string): void {
    this.query.set(value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 250);
  }

  setFilter(f: TypeFilter): void {
    this.typeFilter.set(f);
  }

  visibleRows(): DbStudy[] {
    const f = this.typeFilter();
    return f === 'all' ? this.rows() : this.rows().filter((s) => s.doc_type === f);
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.svc.adminList(this.query()).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Studies API is not reachable.');
      },
    });
  }

  setDisplay(s: DbStudy): void {
    this.svc.setDisplay(s.ref, !s.displayed).subscribe({
      next: (r) => {
        this.rows.update((rows) => rows.map((row) =>
          row.ref === s.ref
            ? { ...row, display_date: r.display_date, displayed: r.displayed }
            : row));
        this.svc.reload();
      },
      error: () => this.error.set(`Could not update display for ${s.ref}.`),
    });
  }

  setPin(s: DbStudy): void {
    this.svc.setPin(s.ref, !s.pinned).subscribe({
      next: (r) => {
        this.rows.update((rows) => rows.map((row) =>
          row.ref === s.ref ? { ...row, pinned: r.pinned } : row));
        this.svc.reload();
      },
      error: () => this.error.set(`Could not update pin for ${s.ref}.`),
    });
  }

  toggleExpand(s: DbStudy): void {
    const next = new Set(this.expanded());
    if (next.has(s.ref)) next.delete(s.ref); else next.add(s.ref);
    this.expanded.set(next);
  }

  isExpanded(s: DbStudy): boolean {
    return this.expanded().has(s.ref);
  }

  pdfUrl(ref: string): string {
    return this.svc.pdfUrl(ref);
  }
}
