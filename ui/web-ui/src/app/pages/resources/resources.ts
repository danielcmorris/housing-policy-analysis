import { Component } from '@angular/core';
import { RESOURCE_GROUPS } from '../../core/site.data';

@Component({
  selector: 'app-resources',
  imports: [],
  templateUrl: './resources.html',
})
export class ResourcesPage {
  readonly resourceGroups = RESOURCE_GROUPS;
}
