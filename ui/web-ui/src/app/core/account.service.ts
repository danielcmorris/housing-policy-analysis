import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CanActivateFn } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { firstValueFrom } from 'rxjs';
import { filter } from 'rxjs/operators';
import { API_BASE } from './legislation.service';

/* The user-manager module's client side. Auth0 owns identity (login, tokens);
   the API owns authorization: a local users row per account with the role
   ('admin' | 'member') and disabled flag. GET /api/auth/config says whether
   auth is enforced at all — when it isn't (Auth:Enabled=false), everything
   behaves exactly as before the module existed. */

export interface AuthConfig {
  enabled: boolean;
  domain: string;
  client_id: string;
  audience: string;
}

export interface Me {
  user_id?: number;
  sub: string;
  email: string | null;
  name: string | null;
  picture: string | null;
  role: 'admin' | 'member' | string;
  disabled: boolean;
}

export interface ManagedUser extends Me {
  user_id: number;
  first_login: string;
  last_login: string;
}

@Injectable({ providedIn: 'root' })
export class AccountService {
  private http = inject(HttpClient);
  private auth0 = inject(AuthService);

  /** null until /api/auth/config answers. */
  readonly config = signal<AuthConfig | null>(null);
  readonly me = signal<Me | null>(null);
  readonly authenticated = signal(false);

  readonly enabled = computed(() => this.config()?.enabled ?? false);
  readonly isAdmin = computed(() => !this.enabled() || this.me()?.role === 'admin');
  readonly displayName = computed(() => {
    const m = this.me();
    return m?.name || m?.email || 'Account';
  });

  private configPromise: Promise<AuthConfig>;

  constructor() {
    this.configPromise = firstValueFrom(this.http.get<AuthConfig>(`${API_BASE}/auth/config`))
      .catch(() => ({ enabled: false, domain: '', client_id: '', audience: '' }));
    this.configPromise.then((c) => this.config.set(c));

    this.auth0.isAuthenticated$.subscribe((v) => this.authenticated.set(v));
    // After login, report the OIDC profile (access tokens usually omit
    // email/name) — the API upserts the users row and returns the role.
    this.auth0.user$.pipe(filter((u) => !!u)).subscribe((u) => {
      this.http.post<Me>(`${API_BASE}/users/me`, {
        email: u!.email ?? null, name: u!.name ?? null, picture: u!.picture ?? null,
      }).subscribe({
        next: (me) => this.me.set(me),
        error: () => this.me.set(null),
      });
    });
  }

  login(targetUrl?: string): void {
    this.auth0.loginWithRedirect({ appState: { target: targetUrl || '/' } });
  }

  logout(): void {
    this.me.set(null);
    this.auth0.logout({ logoutParams: { returnTo: window.location.origin } });
  }

  /** Route-guard core: resolve whether the current user may enter /admin. */
  async canAdmin(targetUrl: string): Promise<boolean> {
    const cfg = await this.configPromise;
    if (!cfg.enabled) return true;
    await firstValueFrom(this.auth0.isLoading$.pipe(filter((l) => !l)));
    const authed = await firstValueFrom(this.auth0.isAuthenticated$);
    if (!authed) {
      this.login(targetUrl);
      return false;
    }
    if (this.me() === null) {
      // First navigation after login: wait for the users/me round trip.
      const me = await firstValueFrom(this.http.get<Me>(`${API_BASE}/users/me`))
        .catch(() => null);
      this.me.set(me);
    }
    return this.me()?.role === 'admin' && !this.me()?.disabled;
  }

  // --- admin: manage users -------------------------------------------------

  listUsers(q: string) {
    const params = new URLSearchParams();
    if (q.trim()) params.set('q', q.trim());
    return this.http.get<ManagedUser[]>(`${API_BASE}/admin/users?${params}`);
  }

  setRole(userId: number, role: string) {
    return this.http.post<{ user_id: number; role: string }>(
      `${API_BASE}/admin/users/${userId}/role`, { role });
  }

  setDisabled(userId: number, disabled: boolean) {
    return this.http.post<{ user_id: number; disabled: boolean }>(
      `${API_BASE}/admin/users/${userId}/disabled`, { disabled });
  }
}

/** Functional guard for the /admin routes. */
export const adminGuard: CanActivateFn = (_route, state) =>
  inject(AccountService).canAdmin(state.url);
