import { Component, inject } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
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
  private router = inject(Router);
  readonly footerCols = FOOTER_COLS;

  search(value: string, input: HTMLInputElement): void {
    const q = value.trim();
    if (!q) return;
    input.value = '';
    this.router.navigate(['/search'], { queryParams: { q } });
  }

  readonly navItems = [
    { label: 'Home', path: '/', exact: true },
    { label: 'Studies Library', path: '/studies', exact: false },
    { label: 'Data Commons', path: '/commons', exact: false },
    { label: 'US Congress', path: '/congress', exact: false },
    { label: 'City Legislation', path: '/city', exact: false },
    { label: 'Resources', path: '/resources', exact: false },
    { label: 'About', path: '/about', exact: false },
  ];
}
