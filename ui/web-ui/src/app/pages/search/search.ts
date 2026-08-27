import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { SearchDoc, SearchResponse, SearchService } from '../../core/search.service';

@Component({
  selector: 'app-search',
  imports: [RouterLink],
  templateUrl: './search.html',
})
export class SearchPage {
  private svc = inject(SearchService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  readonly query = signal('');
  readonly loading = signal(false);
  readonly response = signal<SearchResponse | null>(null);
  readonly error = signal('');

  constructor() {
    this.route.queryParamMap.subscribe((p) => {
      const q = (p.get('q') ?? '').trim();
      this.query.set(q);
      if (q) this.run(q);
      else this.response.set(null);
    });
  }

  submit(value: string): void {
    const q = value.trim();
    if (!q) return;
    this.router.navigate(['/search'], { queryParams: { q } });
  }

  private run(q: string): void {
    this.loading.set(true);
    this.error.set('');
    this.svc.search(q).subscribe({
      next: (r) => {
        this.response.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Search is unavailable — the API could not be reached.');
      },
    });
  }

  sourceLabel(d: SearchDoc): string {
    switch (d.source_type) {
      case 'federal_bill': return 'Federal';
      case 'city_matter': return d.jurisdiction || 'City';
      case 'study': return 'Study';
      default: return d.source_type;
    }
  }

  sourceColor(d: SearchDoc): string {
    switch (d.source_type) {
      case 'federal_bill': return 'var(--accent)';
      case 'city_matter': return 'var(--gold)';
      case 'study': return 'var(--ok)';
      default: return 'var(--text-3)';
    }
  }

  byline(d: SearchDoc): string {
    const parts: string[] = [];
    if (d.source_type === 'study' && d.jurisdiction) parts.push(d.jurisdiction);
    if (d.doc_year != null) parts.push(String(d.doc_year));
    parts.push(d.source_key);
    return parts.join(' · ');
  }
}
