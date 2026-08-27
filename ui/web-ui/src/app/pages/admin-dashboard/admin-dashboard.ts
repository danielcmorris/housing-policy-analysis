import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  AdminStats, BillCandidate, LegislationService, RefreshResult, STATUS_LABEL,
} from '../../core/legislation.service';

@Component({
  selector: 'app-admin-dashboard',
  imports: [RouterLink, DatePipe],
  templateUrl: './admin-dashboard.html',
})
export class AdminDashboardPage {
  private svc = inject(LegislationService);

  readonly stats = signal<AdminStats | null>(null);
  readonly statusLabel = STATUS_LABEL;

  readonly refreshing = signal(false);
  readonly refreshResult = signal<RefreshResult | null>(null);
  readonly refreshError = signal('');

  readonly discovering = signal(false);
  readonly discoverDays = signal(30);
  readonly candidates = signal<BillCandidate[] | null>(null);
  readonly discoverMeta = signal('');
  readonly discoverError = signal('');
  /* candidate key -> 'adding' | 'tracked' | 'untracked' | error message */
  readonly candidateState = signal<Record<string, string>>({});

  constructor() {
    this.loadStats();
  }

  loadStats(): void {
    this.svc.stats().subscribe({
      next: (s) => this.stats.set(s),
      error: () => this.stats.set(null),
    });
  }

  runRefresh(): void {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.refreshError.set('');
    this.refreshResult.set(null);
    this.svc.refresh().subscribe({
      next: (r) => {
        this.refreshing.set(false);
        this.refreshResult.set(r);
        this.loadStats();
        this.svc.reload();
      },
      error: (e) => {
        this.refreshing.set(false);
        this.refreshError.set(e?.error?.detail || 'Refresh failed — is the API running?');
      },
    });
  }

  runDiscover(): void {
    if (this.discovering()) return;
    this.discovering.set(true);
    this.discoverError.set('');
    this.candidates.set(null);
    this.candidateState.set({});
    this.svc.discover(this.discoverDays()).subscribe({
      next: (r) => {
        this.discovering.set(false);
        this.candidates.set(r.candidates);
        this.discoverMeta.set(
          `Scanned ${r.listed} recently-updated bills (${r.detail_calls} detail fetches) over ${r.days} days.`,
        );
        this.loadStats();
      },
      error: (e) => {
        this.discovering.set(false);
        this.discoverError.set(e?.error?.detail || 'Discovery failed — is the API running?');
      },
    });
  }

  key(c: BillCandidate): string {
    return `${c.congress}-${c.bill_type}-${c.bill_number}`;
  }

  add(c: BillCandidate, tracked: boolean): void {
    const k = this.key(c);
    if (['adding', 'tracked', 'untracked'].includes(this.candidateState()[k])) return;
    this.candidateState.update((s) => ({ ...s, [k]: 'adding' }));
    this.svc.addBill(c, tracked).subscribe({
      next: (r) => {
        this.candidateState.update((s) => ({ ...s, [k]: r.tracking_status }));
        this.loadStats();
        if (tracked) this.svc.reload();
      },
      error: (e) => {
        this.candidateState.update((s) => ({ ...s, [k]: e?.error?.detail || 'failed' }));
      },
    });
  }

  stateOf(c: BillCandidate): string {
    return this.candidateState()[this.key(c)] || '';
  }
}
