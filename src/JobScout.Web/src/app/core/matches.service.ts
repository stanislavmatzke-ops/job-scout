import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MatchDetail, MatchList, MatchStatus, MatchView } from './matches.models';

/** /api/matches – the user's jobs (only ever their own – the API checks the owner). */
@Injectable({ providedIn: 'root' })
export class MatchesService {
  private readonly http = inject(HttpClient);

  list(view: MatchView): Promise<MatchList> {
    return firstValueFrom(this.http.get<MatchList>('/api/matches', { params: { view } }));
  }

  detail(id: string): Promise<MatchDetail> {
    return firstValueFrom(this.http.get<MatchDetail>(`/api/matches/${id}`));
  }

  changeStatus(id: string, status: MatchStatus, note: string | null): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/matches/${id}/status`, { status, note }));
  }

  updateNotes(id: string, notes: string | null): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/matches/${id}/notes`, { notes }));
  }
}
