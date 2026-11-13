import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** GET /api/site – who runs this instance (Operator:* in the API's configuration). */
export interface SiteInfo {
  operatorName: string;
  operatorEmail: string;
  aboutUrl: string;
}

/** Loaded once for the privacy page and the footer; public – no sign-in needed. */
@Injectable({ providedIn: 'root' })
export class SiteService {
  private readonly http = inject(HttpClient);
  private readonly current = signal<SiteInfo | null>(null);
  private readonly failedState = signal(false);
  private loading: Promise<void> | null = null;

  readonly site = this.current.asReadonly();
  readonly failed = this.failedState.asReadonly();

  load(): Promise<void> {
    this.loading ??= firstValueFrom(this.http.get<SiteInfo>('/api/site'))
      .then(site => this.current.set(site))
      .catch(() => {
        this.failedState.set(true);
        this.loading = null;                                // a later page may try again
      });
    return this.loading;
  }
}
