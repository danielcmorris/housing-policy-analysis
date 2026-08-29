import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CityMatterDetail, CityService, legistarMatterUrl, statusBucket } from '../../core/city.service';

const BUCKET_STYLE: Record<string, { color: string; bg: string }> = {
  ok:      { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  warn:    { color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  neutral: { color: 'var(--text-3)', bg: 'var(--neutral-bg)' },
};

/* City matter detail — the full Legistar record for one tracked ordinance,
   resolution, or motion, including the stored matter text when available. */

@Component({
  selector: 'app-city-matter',
  imports: [DatePipe, RouterLink],
  templateUrl: './city-matter.html',
})
export class CityMatterPage {
  private city = inject(CityService);
  private route = inject(ActivatedRoute);

  readonly matter = signal<CityMatterDetail | null>(null);
  readonly resolved = signal(false);

  constructor() {
    this.route.paramMap.subscribe((p) => {
      const id = p.get('id');
      this.matter.set(null);
      this.resolved.set(false);
      if (!id) { this.resolved.set(true); return; }
      this.city.get(id).subscribe({
        next: (m) => { this.matter.set(m); this.resolved.set(true); },
        error: () => this.resolved.set(true),
      });
    });
  }

  pillStyle(m: CityMatterDetail): { color: string; bg: string } {
    return BUCKET_STYLE[statusBucket(m.status)];
  }

  legistarUrl(m: CityMatterDetail): string {
    return legistarMatterUrl(m);
  }

  facts(m: CityMatterDetail): { k: string; v: string }[] {
    const date = (iso: string | null) => iso
      ? new Date(iso.slice(0, 10) + 'T00:00:00').toLocaleDateString('en', { year: 'numeric', month: 'short', day: 'numeric' })
      : null;
    return [
      { k: 'File number', v: m.matter_file },
      { k: 'Type', v: m.matter_type },
      { k: 'City', v: m.city_name },
      { k: 'Body', v: m.body_name },
      { k: 'Status', v: m.status },
      { k: 'Introduced', v: date(m.intro_date) },
      { k: 'On agenda', v: date(m.agenda_date) },
      { k: 'Passed', v: date(m.passed_date) },
      { k: 'Enactment no.', v: m.enactment_number },
      { k: 'Last modified', v: date(m.last_modified) },
    ].filter((f): f is { k: string; v: string } => !!f.v);
  }
}
