import { Component, inject } from '@angular/core';
import { Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { ThemeService } from './core/theme.service';
import { AccountService } from './core/account.service';
import { FOOTER_COLS } from './core/site.data';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  readonly theme = inject(ThemeService);
  readonly account = inject(AccountService);
  private router = inject(Router);
  readonly footerCols = FOOTER_COLS;

  constructor() {
    // After the Auth0 redirect lands back here, resume the originally
    // requested URL (the guard stashes it in appState.target before login).
    inject(AuthService).appState$.subscribe((s) => {
      if (s?.target) this.router.navigateByUrl(s.target);
    });
  }

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
