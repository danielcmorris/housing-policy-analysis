import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'cuhpr-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly dark = signal<boolean>(this.load());

  constructor() {
    this.apply(this.dark());
  }

  toggle(): void {
    const next = !this.dark();
    this.dark.set(next);
    try { localStorage.setItem(STORAGE_KEY, next ? 'dark' : 'light'); } catch {}
    this.apply(next);
  }

  private load(): boolean {
    try { return localStorage.getItem(STORAGE_KEY) === 'dark'; } catch { return false; }
  }

  private apply(dark: boolean): void {
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
  }
}
