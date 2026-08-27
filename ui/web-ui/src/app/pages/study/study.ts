import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/* Study detail — the prototype ships one fully-authored study page
   (Minneapolis 2040, CUHPR-2026-0142) as the visual reference of record.
   Production drives this from GET /api/studies/{ref}. */

@Component({
  selector: 'app-study',
  imports: [RouterLink],
  templateUrl: './study.html',
})
export class StudyPage {
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
