import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CityMatter, CityService, statusBucket } from '../../core/city.service';

const BUCKET_STYLE: Record<string, { color: string; bg: string }> = {
  ok:      { color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  warn:    { color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  neutral: { color: 'var(--text-3)', bg: 'var(--neutral-bg)' },
};

@Component({
  selector: 'app-city',
  imports: [DatePipe, RouterLink],
  templateUrl: './city.html',
})
export class CityPage {
  private city = inject(CityService);

  readonly query = signal('');
  readonly typeFilter = signal('');
  readonly live = this.city.live;
  readonly cities = this.city.cities;

  readonly matterTypes = computed(() =>
    [...new Set(this.city.matters().map((m) => m.matter_type).filter(Boolean))].sort() as string[],
  );

  readonly matters = computed(() => {
    const q = this.query().toLowerCase().trim();
    const tokens = q.split(/[^a-z0-9.]+/).filter((w) => w.length > 1);
    const type = this.typeFilter();
    let all = this.city.matters();
    if (type) all = all.filter((m) => m.matter_type === type);
    if (!tokens.length) return all;
    return all.filter((m) => {
      const hay = [m.title, m.matter_file, m.matter_type, m.status, m.body_name,
                   m.city_name, ...(m.tags ?? [])]
        .filter(Boolean).join(' ').toLowerCase();
      return tokens.some((t) => hay.includes(t));
    });
  });

  clear(): void {
    this.query.set('');
  }

  pillStyle(m: CityMatter): { color: string; bg: string } {
    return BUCKET_STYLE[statusBucket(m.status)];
  }

  excerpt(m: CityMatter): string {
    const s = (m.title || '').trim();
    return s.length > 260 ? s.slice(0, 260).replace(/\s+\S*$/, '') + '…' : s;
  }

  heading(m: CityMatter): string {
    const s = (m.title || '').trim();
    const cut = s.search(/[;—]| - /);
    const head = cut > 20 ? s.slice(0, cut) : s;
    return head.length > 110 ? head.slice(0, 110).replace(/\s+\S*$/, '') + '…' : head;
  }
}
