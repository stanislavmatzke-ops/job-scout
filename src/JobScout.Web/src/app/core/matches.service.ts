import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AddJobRequest, AddJobResult, MatchDetail, MatchList, MatchStatus, MatchView } from './matches.models';

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

  /** The same status (and note) for several jobs at once; returns how many of them were the user's. */
  changeStatusMany(ids: string[], status: MatchStatus, note: string | null): Promise<{ changed: number }> {
    return firstValueFrom(this.http.post<{ changed: number }>('/api/matches/status', { ids, status, note }));
  }

  updateNotes(id: string, notes: string | null): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/matches/${id}/notes`, { notes }));
  }

  /** A job ad from elsewhere (ADR-0013) – the AI reads it (and evaluates it, if asked); takes up to half a minute. */
  addManually(request: AddJobRequest): Promise<AddJobResult> {
    const form = new FormData();
    if (request.text) form.append('text', request.text);
    if (request.url) form.append('url', request.url);
    if (request.file) form.append('file', request.file, request.file.name);
    form.append('status', request.status);
    form.append('evaluate', String(request.evaluate));
    return firstValueFrom(this.http.post<AddJobResult>('/api/matches/manual', form));
  }

  // ---------- on demand (6c) – these call the AI and take a few seconds; they return the fresh detail ----------

  /** Any job of mine – waiting, triaged out, or again. */
  evaluate(id: string): Promise<MatchDetail> {
    return firstValueFrom(this.http.post<MatchDetail>(`/api/matches/${id}/evaluate`, null));
  }

  /** A letter for an evaluated job; replace = overwrite the existing one (maybe edited by the user). */
  writeLetter(id: string, replace: boolean): Promise<MatchDetail> {
    return firstValueFrom(this.http.post<MatchDetail>(`/api/matches/${id}/letter`, null, { params: { replace } }));
  }

  /** The user's own edit of the letter – no AI. */
  updateLetter(id: string, text: string): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/matches/${id}/letter`, { text }));
  }
}
