import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  LegislationService, STATUS_DETAIL, STATUS_INFO, STATUS_LABEL, STATUS_STYLE, TrackerBill,
} from '../../core/legislation.service';

type TrackingFilter = 'all' | 'tracked' | 'untracked';

@Component({
  selector: 'app-admin-bills',
  imports: [RouterLink, DatePipe],
  templateUrl: './admin-bills.html',
})
export class AdminBillsPage {
  private svc = inject(LegislationService);

  readonly query = signal('');
  readonly tracking = signal<TrackingFilter>('all');
  readonly rows = signal<TrackerBill[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal<Record<string, boolean>>({});

  readonly statusLabel = STATUS_LABEL;
  readonly statusStyle = STATUS_STYLE;
  readonly statusInfo = STATUS_INFO;
  readonly statusDetail = STATUS_DETAIL;
  readonly expanded = signal<Set<string>>(new Set());
  readonly filters: [TrackingFilter, string][] = [
    ['all', 'All'], ['tracked', 'Tracked'], ['untracked', 'Untracked'],
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

  setFilter(f: TrackingFilter): void {
    this.tracking.set(f);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.svc.search(this.query(), this.tracking()).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Tracker API is not reachable at localhost:8000.');
      },
    });
  }

  readonly refreshing = signal<Record<string, boolean>>({});

  setDisplay(b: TrackerBill): void {
    const target = !b.displayed;
    this.svc.setDisplay(b.bill_id, target).subscribe({
      next: (r) => {
        this.rows.update((rows) => rows.map((row) =>
          row.bill_id === b.bill_id
            ? { ...row, display_date: r.display_date, displayed: !!r.display_date }
            : row));
        this.svc.reload();
      },
      error: () => this.error.set(`Could not update display for ${b.ref}.`),
    });
  }

  setPin(b: TrackerBill): void {
    const target = !b.pinned;
    this.svc.setPin(b.bill_id, target).subscribe({
      next: (r) => {
        this.rows.update((rows) => rows.map((row) =>
          row.bill_id === b.bill_id ? { ...row, pinned: r.pinned } : row));
        this.svc.reload();
      },
      error: () => this.error.set(`Could not update pin for ${b.ref}.`),
    });
  }

  refreshBill(b: TrackerBill): void {
    if (this.refreshing()[b.bill_id]) return;
    this.refreshing.update((s) => ({ ...s, [b.bill_id]: true }));
    this.svc.refreshBill(b.bill_id).subscribe({
      next: () => {
        this.refreshing.update((s) => ({ ...s, [b.bill_id]: false }));
        this.load();
        this.svc.reload();
      },
      error: () => {
        this.refreshing.update((s) => ({ ...s, [b.bill_id]: false }));
        this.error.set(`Could not refresh ${b.ref}.`);
      },
    });
  }

  toggleExpand(b: TrackerBill): void {
    const next = new Set(this.expanded());
    if (next.has(b.bill_id)) next.delete(b.bill_id); else next.add(b.bill_id);
    this.expanded.set(next);
  }

  isExpanded(b: TrackerBill): boolean {
    return this.expanded().has(b.bill_id);
  }

  toggle(b: TrackerBill): void {
    if (this.busy()[b.bill_id]) return;
    const target = b.tracking_status !== 'tracked';
    this.busy.update((s) => ({ ...s, [b.bill_id]: true }));
    this.svc.setTracking(b.bill_id, target).subscribe({
      next: (r) => {
        this.busy.update((s) => ({ ...s, [b.bill_id]: false }));
        this.rows.update((rows) => rows.map((row) =>
          row.bill_id === b.bill_id
            ? { ...row, tracking_status: r.tracking_status, has_text: row.has_text || r.texts_pulled }
            : row));
        this.svc.reload();
      },
      error: () => {
        this.busy.update((s) => ({ ...s, [b.bill_id]: false }));
        this.error.set(`Could not update ${b.ref}.`);
      },
    });
  }
}
