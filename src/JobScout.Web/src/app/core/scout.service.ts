import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** searching = the first search runs in the background right now (6h); lastSearchAt = the last successful search. */
export interface ScoutStatus {
  searching: boolean;
  lastSearchAt: string | null;
}

/** How often the jobs page asks while the first search runs – it takes minutes, not seconds. */
export const SCOUT_POLL_MS = 15_000;

@Injectable({ providedIn: 'root' })
export class ScoutService {
  private readonly http = inject(HttpClient);

  status(): Promise<ScoutStatus> {
    return firstValueFrom(this.http.get<ScoutStatus>('/api/scout/status'));
  }
}
