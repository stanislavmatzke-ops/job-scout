import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { FeedbackKind } from './admin.models';

export const MAX_FEEDBACK = 4000;      // = Feedback.MaxMessageLength

export interface FeedbackMessage {
  kind: FeedbackKind;
  rating: number | null;
  message: string;
  replyWanted: boolean;
}

/** POST /api/feedback – stored and e-mailed to the author of the app. */
@Injectable({ providedIn: 'root' })
export class FeedbackService {
  private readonly http = inject(HttpClient);

  send(feedback: FeedbackMessage): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/feedback', feedback));
  }
}
