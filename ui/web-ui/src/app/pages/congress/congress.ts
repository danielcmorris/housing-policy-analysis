import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import {
  LegislationService, REVIEWED_BILLS, STATUS_LABEL, STATUS_STYLE, TrackerBill,
} from '../../core/legislation.service';

@Component({
  selector: 'app-congress',
  imports: [DatePipe],
  templateUrl: './congress.html',
})
export class CongressPage {
  private legislation = inject(LegislationService);
  private router = inject(Router);

  readonly query = signal('');
  readonly live = this.legislation.live;
  readonly statusStyle = STATUS_STYLE;
  readonly statusLabel = STATUS_LABEL;

  readonly bills = computed(() => {
    const q = this.query().toLowerCase().trim();
    const tokens = q.split(/[^a-z0-9.]+/).filter((w) => w.length > 1);
    const all = this.legislation.bills();
    if (!tokens.length) return all;
    return all.filter((b) => {
      const hay = [b.title, b.ref, b.category, b.summary, b.sponsor, b.status_text]
        .filter(Boolean).join(' ').toLowerCase();
      return tokens.some((t) => hay.includes(t));
    });
  });

  clear(): void {
    this.query.set('');
  }

  isFeatured(b: TrackerBill): boolean {
    return b.bill_id in REVIEWED_BILLS;
  }

  open(b: TrackerBill): void {
    const reviewId = REVIEWED_BILLS[b.bill_id];
    if (reviewId) {
      this.router.navigate(['/bills', reviewId]);
    } else {
      window.open(b.congress_gov_url, '_blank', 'noopener');
    }
  }

  excerpt(b: TrackerBill): string {
    const s = (b.summary || '').trim();
    if (!s) return 'Tracked pending review. Full text and status are synchronized from Congress.gov.';
    return s.length > 220 ? s.slice(0, 220).replace(/\s+\S*$/, '') + '…' : s;
  }

  sponsorLine(b: TrackerBill): string {
    return (b.sponsor || '').replace(/\s*\[.*\]\s*$/, (m) => {
      const inner = m.trim().slice(1, -1);
      return ` (${inner})`;
    });
  }
}
