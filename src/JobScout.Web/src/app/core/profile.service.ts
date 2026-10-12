import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GrammaticalGender, ProfileView } from './profile.models';
import { t } from './i18n';

export const CV_EXTENSIONS = ['.pdf', '.md', '.txt'];
export const CV_MAX_BYTES = 5 * 1024 * 1024;   // = StoredCv.MaxBytes on the server

/** Checked in the browser too – the user learns at once, and we don't upload 50 MB just to hear "too big". */
export function cvProblem(file: File): string | null {
  const name = file.name.toLowerCase();
  if (!CV_EXTENSIONS.some(ext => name.endsWith(ext))) return t().profile.cvFormats;
  if (file.size === 0) return t().profile.cvEmpty;
  if (file.size > CV_MAX_BYTES) return t().profile.cvTooBig;
  return null;
}

/** /api/profile – CV, notes and settings. Uploading the CV or notes runs the AI analysis (if something changed). */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);

  get(): Promise<ProfileView> {
    return firstValueFrom(this.http.get<ProfileView>('/api/profile'));
  }

  /**
   * multipart/form-data with the field "file". The Content-Type header is NOT set by hand –
   * the browser adds it together with the boundary that separates the parts.
   */
  uploadCv(file: File): Promise<ProfileView> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.put<ProfileView>('/api/profile/cv', form));
  }

  updateNotes(notes: string | null): Promise<ProfileView> {
    return firstValueFrom(this.http.put<ProfileView>('/api/profile/notes', { notes }));
  }

  updateMorningEmail(on: boolean): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/profile/settings', { morningEmail: on }));
  }

  updateGender(gender: GrammaticalGender): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/profile/settings', { gender }));
  }
}
