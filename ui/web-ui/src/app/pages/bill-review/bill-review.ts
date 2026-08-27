import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { BillsService } from '../../core/bills.service';
import { billStatus, conf, rec, STAGE } from '../../core/maps';

@Component({
  selector: 'app-bill-review',
  imports: [RouterLink],
  templateUrl: './bill-review.html',
})
export class BillReviewPage {
  private bills = inject(BillsService);
  private route = inject(ActivatedRoute);

  private id = toSignal(this.route.paramMap.pipe(map((p) => p.get('id') ?? 'hr6644-119')), { initialValue: 'hr6644-119' });

  readonly vm = computed(() => {
    const data = this.bills.content(this.id());
    if (!data) return null;
    const { meta, c } = data;
    const st = billStatus(meta.status);
    const outlookConf = conf(c.outlook.confidence ?? meta.overallConfidence);
    return {
      meta, c, st, outlookConf,
      precedents: (c.precedents ?? []).map((p) => ({ ...p, badge: conf(p.confidence) })),
      projections: (c.projections ?? []).map((p) => ({ ...p, badge: conf(p.confidence) })),
      legStatus: (meta.legislativeStatus ?? []).map((s) => ({
        icon: s.state === 'complete' ? 'check_circle' : (s.state === 'in_progress' ? 'pending' : 'radio_button_unchecked'),
        color: s.state === 'complete' ? 'var(--ok)' : (s.state === 'in_progress' ? 'var(--warn)' : 'var(--text-4)'),
        label: (STAGE[s.stage] ?? s.stage) + (s.vote ? (' · ' + s.vote.yea + '–' + s.vote.nay) : ''),
        date: s.date ? this.fmtDate(s.date) : (s.state === 'in_progress' ? 'In progress' : ''),
      })),
      reviews: (c.reviews ?? []).map((r) => ({ ...r, badge: rec(r.recommendation) })),
      endorseCount: (c.reviews ?? []).filter((r) => r.recommendation === 'endorse').length,
    };
  });

  private fmtDate(iso: string): string {
    try { return new Date(iso + 'T00:00:00').toLocaleDateString('en', { year: 'numeric', month: 'short', day: 'numeric' }); }
    catch { return iso; }
  }
}
