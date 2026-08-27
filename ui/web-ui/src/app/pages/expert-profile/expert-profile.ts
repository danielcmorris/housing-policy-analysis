import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Expert, ExpertProfile, ExpertsService, mapApiExpert } from '../../core/experts.service';

@Component({
  selector: 'app-expert-profile',
  imports: [RouterLink, DatePipe],
  templateUrl: './expert-profile.html',
})
export class ExpertProfilePage {
  readonly svc = inject(ExpertsService);
  private route = inject(ActivatedRoute);

  readonly expert = signal<Expert | null>(null);
  readonly profile = signal<ExpertProfile | null>(null);
  readonly missing = signal(false);

  constructor() {
    this.route.paramMap.subscribe((p) => {
      const slug = p.get('slug');
      this.expert.set(null);
      this.profile.set(null);
      this.missing.set(false);
      if (!slug) { this.missing.set(true); return; }
      this.svc.get(slug).subscribe({
        next: (prof) => {
          this.profile.set(prof);
          this.expert.set(mapApiExpert(prof.expert));
        },
        error: () => this.missing.set(true),
      });
    });
  }

  hideImg(e: Event): void {
    try { (e.target as HTMLElement).style.display = 'none'; } catch {}
  }

  recLabel(rec: string | null): string {
    return ({
      accept: 'Accept', endorse: 'Endorse Analysis', minor_revisions: 'Minor revisions',
      major_revisions: 'Major revisions', reject: 'Reject',
    } as Record<string, string>)[rec ?? ''] ?? (rec ?? '');
  }
}
