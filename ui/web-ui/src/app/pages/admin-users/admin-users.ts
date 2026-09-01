import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AccountService, ManagedUser } from '../../core/account.service';

/* User manager: every account that has signed in through Auth0, with the
   locally owned role and disabled flag. Identity edits (password, email, MFA)
   happen in the Auth0 dashboard — this page manages what those identities may
   do here. */
@Component({
  selector: 'app-admin-users',
  imports: [RouterLink, DatePipe],
  templateUrl: './admin-users.html',
})
export class AdminUsersPage {
  readonly account = inject(AccountService);

  readonly query = signal('');
  readonly rows = signal<ManagedUser[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal<Record<number, boolean>>({});

  readonly roles = ['admin', 'member'];

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.load();
  }

  onQuery(value: string): void {
    this.query.set(value);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 250);
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.account.listUsers(this.query()).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.error.set(e?.error?.detail || 'Could not load users.');
      },
    });
  }

  /** The signed-in admin's own row — guarded so they can't lock themselves out. */
  isSelf(u: ManagedUser): boolean {
    return !!this.account.me()?.sub && u.sub === this.account.me()!.sub;
  }

  setRole(u: ManagedUser, role: string): void {
    if (role === u.role || this.busy()[u.user_id]) return;
    this.busy.update((b) => ({ ...b, [u.user_id]: true }));
    this.account.setRole(u.user_id, role).subscribe({
      next: (r) => {
        this.busy.update((b) => ({ ...b, [u.user_id]: false }));
        this.rows.update((rows) => rows.map((row) =>
          row.user_id === u.user_id ? { ...row, role: r.role } : row));
      },
      error: (e) => {
        this.busy.update((b) => ({ ...b, [u.user_id]: false }));
        this.error.set(e?.error?.detail || `Could not change the role for ${u.email || u.sub}.`);
        this.load();
      },
    });
  }

  toggleDisabled(u: ManagedUser): void {
    if (this.busy()[u.user_id]) return;
    this.busy.update((b) => ({ ...b, [u.user_id]: true }));
    this.account.setDisabled(u.user_id, !u.disabled).subscribe({
      next: (r) => {
        this.busy.update((b) => ({ ...b, [u.user_id]: false }));
        this.rows.update((rows) => rows.map((row) =>
          row.user_id === u.user_id ? { ...row, disabled: r.disabled } : row));
      },
      error: (e) => {
        this.busy.update((b) => ({ ...b, [u.user_id]: false }));
        this.error.set(e?.error?.detail || `Could not update ${u.email || u.sub}.`);
      },
    });
  }
}
