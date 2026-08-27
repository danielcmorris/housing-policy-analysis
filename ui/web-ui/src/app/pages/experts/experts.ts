import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExpertsService } from '../../core/experts.service';

@Component({
  selector: 'app-experts',
  imports: [RouterLink],
  templateUrl: './experts.html',
})
export class ExpertsPage {
  readonly svc = inject(ExpertsService);
  readonly cat = signal('all');

  readonly catChips = [
    ['all', 'All'], ['Academic', 'Academic'], ['Think tank', 'Think Tank'],
    ['Research center', 'Research Center'], ['Non-profit', 'Non-Profit'],
  ] as const;

  readonly roster = computed(() => {
    const cat = this.cat();
    return this.svc.experts()
      .filter((p) => cat === 'all' || this.svc.catMeta(p.category).bucket === cat)
      .map((p) => ({ ...p, initials: this.svc.initials(p.name), meta: this.svc.catMeta(p.category) }));
  });

  hideImg(e: Event): void {
    try { (e.target as HTMLElement).style.display = 'none'; } catch {}
  }
}
