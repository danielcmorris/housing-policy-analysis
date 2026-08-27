import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonsEntry, commonsAnswerLead, filterCommons } from '../../core/commons.data';
import { DC_AXES, DC_COVERAGE, DC_STATS, DC_STEPS } from '../../core/site.data';

interface CommonsAnswer { q: string; lead: string; hits: CommonsEntry[]; }

@Component({
  selector: 'app-commons',
  imports: [RouterLink],
  templateUrl: './commons.html',
})
export class CommonsPage {
  readonly dcStats = DC_STATS;
  readonly dcCoverage = DC_COVERAGE;
  readonly dcAxes = DC_AXES;
  readonly dcSteps = DC_STEPS;

  readonly query = signal('');
  readonly level = signal('all');
  readonly status = signal('all');
  readonly category = signal('all');
  readonly answer = signal<CommonsAnswer | null>(null);

  readonly levelChips = [ ['all', 'All levels'], ['federal', 'Federal'], ['state', 'State'], ['city', 'City'] ] as const;
  readonly statusChips = [ ['all', 'Passed & failed'], ['passed', 'Passed'], ['failed', 'Failed'] ] as const;
  readonly categoryChips = [ ['all', 'All categories'], ['Supply-Side', 'Supply-Side'], ['Regulation Reduction', 'Regulation Reduction'], ['Rent Regulation', 'Rent Regulation'], ['Affordability', 'Affordability'] ] as const;

  readonly suggestions = [
    { label: 'Why do statewide zoning bills fail?', q: 'Why do statewide zoning bills fail?' },
    { label: 'Compare rent-control measures that passed vs. failed', q: 'rent control passed and failed' },
    { label: 'Which upzoning laws produced the most supply?', q: 'upzoning supply' },
  ];

  readonly results = computed(() => filterCommons(this.query(), this.level(), this.status(), this.category()));

  onInput(value: string): void {
    this.query.set(value);
  }

  ask(text?: string): void {
    const q = (text ?? this.query()).trim();
    if (!q) return;
    if (text !== undefined) this.query.set(text);
    const hits = this.results().slice(0, 4);
    this.answer.set({ q, lead: commonsAnswerLead(q), hits });
  }

  levelColor(level: string): string {
    return level === 'federal' ? 'var(--accent)' : (level === 'state' ? 'var(--gold)' : 'var(--text-3)');
  }

  levelLabel(level: string): string {
    return level.charAt(0).toUpperCase() + level.slice(1);
  }
}
