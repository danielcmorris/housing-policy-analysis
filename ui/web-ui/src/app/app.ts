import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { ThemeService } from './core/theme.service';
import { FOOTER_COLS } from './core/site.data';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  readonly theme = inject(ThemeService);
  readonly footerCols = FOOTER_COLS;

  readonly navItems = [
    { label: 'Home', path: '/', exact: true },
    { label: 'Studies Library', path: '/studies', exact: false },
    { label: 'Research Assistant', path: '/assistant', exact: false },
    { label: 'Data Commons', path: '/commons', exact: false },
    { label: 'US Congress', path: '/congress', exact: false },
    { label: 'Resources', path: '/resources', exact: false },
    { label: 'About', path: '/about', exact: false },
  ];
}
