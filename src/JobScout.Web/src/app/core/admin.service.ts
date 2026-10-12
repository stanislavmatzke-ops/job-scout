import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AdminUser, CreatedInvitation, Invitation, InvitationLink } from './admin.models';

export const MAX_SUSPEND_REASON = 500;      // = User.MaxSuspendReasonLength
export const MAX_INVITATION_NOTE = 200;     // = AdminEndpoints.MaxInvitationNoteLength

/** AI costs: "—" for nothing, three decimals below a dollar (a few cents a day matter), two above. */
export function formatUsd(value: number): string {
  if (!value) return '—';
  return '$' + value.toFixed(value < 1 ? 3 : 2);
}

/**
 * The sign-in links of an invitation. Built here, not on the server: the browser knows the public address
 * (localhost:4200 behind the dev proxy, the real domain in Azure), the API behind a proxy may not.
 * returnUrl=/ → after sign-in the new user lands on the app, already approved by the invitation.
 */
export function invitationLinks(code: string, origin: string, providers: string[]): InvitationLink[] {
  const labels: Record<string, string> = { google: 'Google', microsoft: 'Microsoft' };
  return providers.map(provider => ({
    provider,
    label: labels[provider] ?? provider,
    url: `${origin}/api/auth/login/${provider}?invite=${encodeURIComponent(code)}&returnUrl=%2F`,
  }));
}

/** /api/admin – only for the admins from configuration (Auth:Admins); everyone else gets 403. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  users(): Promise<AdminUser[]> {
    return firstValueFrom(this.http.get<AdminUser[]>('/api/admin/users'));
  }

  approve(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/admin/users/${id}/approve`, null));
  }

  suspend(id: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/admin/users/${id}/suspend`, { reason }));
  }

  invitations(): Promise<Invitation[]> {
    return firstValueFrom(this.http.get<Invitation[]>('/api/admin/invitations'));
  }

  createInvitation(note: string): Promise<CreatedInvitation> {
    return firstValueFrom(this.http.post<CreatedInvitation>('/api/admin/invitations', { note }));
  }
}
