import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CONGRESS_STATUS_STYLE, filterCongress } from '../../core/congress.data';

@Component({
  selector: 'app-congress',
  imports: [RouterLink],
  templateUrl: './congress.html',
})
export class CongressPage {
  readonly query = signal('');
  readonly bills = computed(() => filterCongress(this.query()));
  readonly statusStyle = CONGRESS_STATUS_STYLE;

  clear(): void {
    this.query.set('');
  }
}
