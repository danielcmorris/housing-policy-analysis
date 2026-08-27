import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BillsService } from '../../core/bills.service';
import { ALL_STUDIES, STUDY_STATUS, TOPICS, clarityColor } from '../../core/studies.data';
import { METHOD_STEPS, TECH_PILLARS, COMMENTARY } from '../../core/site.data';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
})
export class HomePage {
  private bills = inject(BillsService);

  readonly topics = TOPICS;
  readonly steps = METHOD_STEPS;
  readonly techPillars = TECH_PILLARS;
  readonly commentary = COMMENTARY;
  readonly featured = ALL_STUDIES.slice(0, 6);
  readonly studyStatus = STUDY_STATUS;
  readonly clarityColor = clarityColor;

  readonly featuredBillId = this.bills.featuredBillId;
  readonly bill = computed(() => this.bills.content(this.featuredBillId()));
}
