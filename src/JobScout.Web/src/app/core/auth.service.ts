import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthProviders, Me } from './models';

/**
 * Who is signed in – one source of truth for the whole app (GET /api/me).
 * The session itself is an HttpOnly cookie the app never sees (BFF, ADR-0011).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly current = signal<Me | null | undefined>(undefined);   // undefined = not loaded yet, null = signed out

  readonly me = this.current.asReadonly();
  readonly isMember = computed(() => ['Approved', 'Suspended'].includes(this.current()?.access ?? ''));
  readonly isAdmin = computed(() => this.current()?.isAdmin === true);

  /** Loads the user once; later calls reuse it unless forced. */
  async load(force = false): Promise<Me | null> {
    if (!force && this.current() !== undefined) return this.current() ?? null;
    try {
      const me = await firstValueFrom(this.http.get<Me>('/api/me'));
      this.current.set(me);
      return me;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.current.set(null);
        return null;
      }
      throw error;
    }
  }

  providers(): Promise<AuthProviders> {
    return firstValueFrom(this.http.get<AuthProviders>('/api/auth/providers'));
  }

  /** Microsoft/Google: a full page navigation – the API sends the browser to the provider and back. */
  signInWith(provider: string, returnUrl = '/', invite?: string): void {
    const params = new URLSearchParams({ returnUrl });
    if (invite) params.set('invite', invite);
    window.location.href = `/api/auth/login/${encodeURIComponent(provider)}?${params.toString()}`;
  }

  /** Test personas – only when the API has dev sign-in enabled (never in Production). */
  async devSignIn(persona: string): Promise<Me | null> {
    await firstValueFrom(this.http.post<void>(`/api/auth/dev/${encodeURIComponent(persona)}`, null));
    return this.load(true);
  }

  /** "Try it without signing up" – everyone is the same fictional demo user (ADR-0020). */
  async demoSignIn(): Promise<Me | null> {
    await firstValueFrom(this.http.post<void>('/api/auth/demo', null));
    return this.load(true);
  }

  async signOut(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/auth/logout', null));
    this.current.set(null);
  }

  async requestAccess(message: string | null): Promise<Me | null> {
    await firstValueFrom(this.http.post<void>('/api/access/request', { message }));
    return this.load(true);
  }

  /** The API said 401 somewhere – the session has expired. */
  markSignedOut(): void {
    this.current.set(null);
  }
}
