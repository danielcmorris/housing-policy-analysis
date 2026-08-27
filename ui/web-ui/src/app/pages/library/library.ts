import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ALL_STUDIES, STATUS_FILTERS, STUDY_STATUS, clarityColor } from '../../core/studies.data';
import { StudiesService } from '../../core/studies.service';

/* One row of the library list: database documents (added via the admin page)
   merged with the static demo studies. Optional fields render conditionally. */
export interface LibStudy {
  ref: string;
  category: string;
  title: string;
  authors: string;
  year: number | null;
  pages: number | null;
  reviews: number | null;
  clarity: number | null;
  status: string;
  excerpt: string;
  docType: 'study' | 'proposal' | string;
  fromDb: boolean;
}

@Component({
  selector: 'app-library',
  imports: [RouterLink],
  templateUrl: './library.html',
})
export class LibraryPage {
  private db = inject(StudiesService);

  readonly statusFilters = STATUS_FILTERS;
  readonly studyStatus = STUDY_STATUS;
  readonly clarityColor = clarityColor;

  readonly selectedTopics = signal<Set<string>>(new Set());

  /* The library runs on live database documents. The static demo studies
     render only when the database has none (API down or empty). */
  private readonly all = computed<LibStudy[]>(() => {
    const fromDb: LibStudy[] = this.db.studies().map((s) => ({
      ref: s.ref,
      category: s.category ?? 'Uncategorized',
      title: s.title,
      authors: s.authors ?? '',
      year: s.year,
      pages: s.pages,
      reviews: null,
      clarity: s.clarity,
      status: s.status,
      excerpt: s.summary ?? '',
      docType: s.doc_type,
      fromDb: true,
    }));
    if (fromDb.length) {
      return fromDb.sort((a, b) => (b.year ?? 0) - (a.year ?? 0));
    }
    return ALL_STUDIES.map((s) => ({ ...s, docType: 'study', fromDb: false }));
  });

  /* Topic filter rows derived from whatever is actually in the library. */
  readonly topicFilters = computed(() => {
    const counts = new Map<string, number>();
    for (const s of this.all()) counts.set(s.category, (counts.get(s.category) ?? 0) + 1);
    return [...counts.entries()]
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
      .map(([label, count]) => ({ label, count }));
  });

  readonly studies = computed(() => {
    const sel = this.selectedTopics();
    if (!sel.size) return this.all();
    return this.all().filter((s) => sel.has(s.category));
  });

  toggleTopic(label: string): void {
    const next = new Set(this.selectedTopics());
    if (next.has(label)) next.delete(label); else next.add(label);
    this.selectedTopics.set(next);
  }

  clearFilters(): void {
    this.selectedTopics.set(new Set());
  }
}
