import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CityMatter, CityService, legistarMatterUrl, statusBucket } from '../../core/city.service';

type PublishFilter = 'all' | 'published' | 'unpublished';

const BUCKET_STYLE: Record<string, { color: string; bg: string }> = {
  ok:      { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  warn:    { color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  neutral: { color: 'var(--text-3)', bg: 'var(--neutral-bg)' },
};

@Component({
  selector: 'app-admin-cities',
  imports: [RouterLink, DatePipe],
  templateUrl: './admin-cities.html',
})
export class AdminCitiesPage {
  private svc = inject(CityService);

  readonly query = signal('');
  readonly publish = signal<PublishFilter>('all');
  readonly allRows = signal<CityMatter[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal<Record<string, boolean>>({});
  readonly expanded = signal<Set<string>>(new Set());

  readonly filters: [PublishFilter, string][] = [
    ['all', 'All'], ['published', 'Published'], ['unpublished', 'Unpublished'],
  ];

  readonly rows = computed(() => {
    const f = this.publish();
    const all = this.allRows();
    if (f === 'published') return all.filter((m) => !!m.display_date);
    if (f === 'unpublished') return all.filter((m) => !m.display_date);
    return all;
  });

  readonly counts = computed(() => {
    const all = this.allRows();
    return {
      total: all.length,
      published: all.filter((m) => !!m.display_date).length,
      withText: all.filter((m) => m.has_text).length,
      pinned: all.filter((m) => m.pinned).length,
    };
  });

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.load();
  }

  onQuery(value: string): void {
    this.query.set(value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 250);
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.svc.adminList(this.query()).subscribe({
      next: (rows) => {
        this.allRows.set(rows ?? []);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('City matters API is not reachable at localhost:5000.');
      },
    });
  }

  pillStyle(m: CityMatter): { color: string; bg: string } {
    return BUCKET_STYLE[statusBucket(m.status)];
  }

  setDisplay(m: CityMatter): void {
    if (this.busy()[m.city_matter_id]) return;
    const target = !m.display_date;
    this.busy.update((s) => ({ ...s, [m.city_matter_id]: true }));
    this.svc.setDisplay(m.city_matter_id, target).subscribe({
      next: (r) => {
        this.busy.update((s) => ({ ...s, [m.city_matter_id]: false }));
        this.allRows.update((rows) => rows.map((row) =>
          row.city_matter_id === m.city_matter_id ? { ...row, display_date: r.display_date } : row));
        this.svc.reload();
      },
      error: () => {
        this.busy.update((s) => ({ ...s, [m.city_matter_id]: false }));
        this.error.set(`Could not update display for file ${m.matter_file}.`);
      },
    });
  }

  setPin(m: CityMatter): void {
    if (this.busy()[m.city_matter_id]) return;
    const target = !m.pinned;
    this.busy.update((s) => ({ ...s, [m.city_matter_id]: true }));
    this.svc.setPin(m.city_matter_id, target).subscribe({
      next: (r) => {
        this.busy.update((s) => ({ ...s, [m.city_matter_id]: false }));
        this.allRows.update((rows) => rows.map((row) =>
          row.city_matter_id === m.city_matter_id ? { ...row, pinned: r.pinned } : row));
        this.svc.reload();
      },
      error: () => {
        this.busy.update((s) => ({ ...s, [m.city_matter_id]: false }));
        this.error.set(`Could not update pin for file ${m.matter_file}.`);
      },
    });
  }

  toggleExpand(m: CityMatter): void {
    const next = new Set(this.expanded());
    if (next.has(m.city_matter_id)) next.delete(m.city_matter_id); else next.add(m.city_matter_id);
    this.expanded.set(next);
  }

  isExpanded(m: CityMatter): boolean {
    return this.expanded().has(m.city_matter_id);
  }

  legistarUrl(m: CityMatter): string {
    return legistarMatterUrl(m);
  }
}
