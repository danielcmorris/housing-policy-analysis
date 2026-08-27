import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DbStudy, StudiesService, StudyReviewPublic } from '../../core/studies.service';
import { STUDY_STATUS, clarityColor } from '../../core/studies.data';

/* Study / policy-proposal detail. Documents added through the admin page are
   rendered from the database (with a working PDF download); any other ref
   falls back to the static demo study (Minneapolis 2040), which remains the
   visual reference of record for the fully-reviewed layout. */

@Component({
  selector: 'app-study',
  imports: [RouterLink],
  templateUrl: './study.html',
})
export class StudyPage {
  private svc = inject(StudiesService);
  private route = inject(ActivatedRoute);

  readonly db = signal<DbStudy | null>(null);
  readonly dbReviews = signal<StudyReviewPublic[]>([]);
  readonly resolved = signal(false);
  readonly studyStatus = STUDY_STATUS;
  readonly clarityColor = clarityColor;

  constructor() {
    this.route.paramMap.subscribe((p) => {
      const ref = p.get('ref');
      this.resolved.set(false);
      this.db.set(null);
      this.dbReviews.set([]);
      if (!ref) { this.resolved.set(true); return; }
      this.svc.get(ref).subscribe({
        next: (d) => {
          this.db.set(d.study);
          this.dbReviews.set(d.reviews ?? []);
          this.resolved.set(true);
        },
        error: () => this.resolved.set(true),
      });
    });
  }

  recLabel(rec: string | null): string {
    return ({
      accept: 'Accept', minor_revisions: 'Minor revisions',
      major_revisions: 'Major revisions', reject: 'Reject',
    } as Record<string, string>)[rec ?? ''] ?? (rec ?? '');
  }

  expertInitials(name: string): string {
    const parts = name.trim().split(/\s+/);
    return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
  }

  pdfUrl(ref: string): string {
    return this.svc.pdfUrl(ref);
  }

  typeLabel(s: DbStudy): string {
    return s.doc_type === 'proposal' ? 'Policy Proposal' : 'Study';
  }

  readonly reviewTimeline = [
    { icon: 'check_circle', color: 'var(--ok)', label: 'Intake complete', date: 'Jan 2026' },
    { icon: 'check_circle', color: 'var(--ok)', label: 'AI analysis attached', date: 'Feb 2026' },
    { icon: 'check_circle', color: 'var(--ok)', label: 'Peer review (5 reviewers)', date: 'Mar 2026' },
    { icon: 'verified', color: 'var(--accent)', label: 'Published', date: 'Mar 2026' },
  ];

  readonly studyReviews = [
    { initials: 'DO', name: 'Dr. Dana Okonkwo', affil: 'RAND Corporation', score: '9 / 10', rec: 'Accept', recColor: 'var(--ok)', recBg: 'var(--ok-bg)', date: 'Mar 2026', text: 'A model application of synthetic control. The leave-one-out donor-pool check added in revision fully addresses my original concern about the two Sun Belt metros.' },
    { initials: 'KM', name: 'Dr. Kwame Mills', affil: 'Brookings Institution', score: '8 / 10', rec: 'Minor revisions', recColor: 'var(--warn)', recBg: 'var(--warn-bg)', date: 'Feb 2026', text: 'Convincing identification overall. The short-run rent null result should be contextualized more explicitly against the four-year observation window before publication.' },
    { initials: 'LT', name: 'Dr. Lan Tran', affil: 'George Mason University', score: '9 / 10', rec: 'Accept', recColor: 'var(--ok)', recBg: 'var(--ok-bg)', date: 'Feb 2026', text: 'The spatial-heterogeneity analysis is the paper’s strongest contribution and is robust to alternative census-tract definitions. Recommend acceptance.' },
  ];
}
