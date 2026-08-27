import { Component } from '@angular/core';
import { PEOPLE, PRINCIPLES } from '../../core/site.data';

@Component({
  selector: 'app-about',
  imports: [],
  templateUrl: './about.html',
})
export class AboutPage {
  readonly principles = PRINCIPLES;
  readonly people = PEOPLE;
}
