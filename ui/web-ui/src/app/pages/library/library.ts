import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ALL_STUDIES, STATUS_FILTERS, STUDY_STATUS, TOPIC_FILTERS, clarityColor } from '../../core/studies.data';

@Component({
  selector: 'app-library',
  imports: [RouterLink],
  templateUrl: './library.html',
})
export class LibraryPage {
  readonly topicFilters = TOPIC_FILTERS;
  readonly statusFilters = STATUS_FILTERS;
  readonly studyStatus = STUDY_STATUS;
  readonly clarityColor = clarityColor;

  readonly selectedTopics = signal<Set<string>>(new Set());

  readonly studies = computed(() => {
    const sel = this.selectedTopics();
    if (!sel.size) return ALL_STUDIES;
    return ALL_STUDIES.filter((s) => sel.has(s.category));
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
