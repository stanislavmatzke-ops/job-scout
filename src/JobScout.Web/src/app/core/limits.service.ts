import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { formatDate } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { lang, t } from './i18n';

/** One counted limit within the sliding 24 hours (ADR-0021). */
export interface AiLimitUsage {
  used: number;
  limit: number;
  left: number;
  /** When a slot frees up again (the oldest click leaves the window); null = nothing used. */
  resetsAt: string | null;
}

/** GET /api/me/limits */
export interface AiLimits {
  unlimited: boolean;
  profile: AiLimitUsage;
  coverLetters: AiLimitUsage;
  evaluations: AiLimitUsage;
  letterRewritesPerJob: number;
  costUsd: number;
  costLimitUsd: number;
  costResetsAt: string | null;
  paused: boolean;
}

export type AiLimitReason = 'Profile' | 'CoverLetter' | 'Evaluation' | 'LetterRewrites' | 'UserCost' | 'AppPaused';

/** The 429 problem of an AI button: why, the number reached, when to try again. */
interface AiLimitProblem {
  reason?: AiLimitReason;
  limit?: number | null;
  retryAt?: string | null;
}

/** "13. 10. 14:32" / "Oct 13, 2026, 14:32" – in the user's language. */
export function formatWhen(iso: string): string {
  return formatDate(iso, t().fmt.dateTime, lang());
}

/** The message for a 429 from an AI button – worded here, so it is in the user's language (the API answers in Czech). */
export function limitMessage(error: unknown): string {
  const problem = (error instanceof HttpErrorResponse ? error.error : null) as AiLimitProblem | null;
  const reason = problem?.reason;
  if (!reason || !(reason in t().limits.reason)) return t().errors.ai429;
  const text = t().limits.reason[reason](problem?.limit ?? 0);
  const retryAt = problem?.retryAt;
  return retryAt ? `${text} ${t().limits.again(formatWhen(retryAt))}` : text;
}

/**
 * Message when nothing more can be started by clicking at all (the money limits) – null = clicks are possible.
 * Shown once above the AI buttons; the per-button counts are in the hints.
 */
export function blockedMessage(limits: AiLimits | null): string | null {
  if (!limits || limits.unlimited) return null;
  if (limits.paused) return t().limits.reason.AppPaused(0);
  if (limits.costUsd >= limits.costLimitUsd) {
    const text = t().limits.reason.UserCost(0);
    return limits.costResetsAt ? `${text} ${t().limits.again(formatWhen(limits.costResetsAt))}` : text;
  }
  return null;
}

/** Text under a button: "2 of 3 left…", or when it frees up again. Nothing for admins (unlimited). */
export function usageHint(limits: AiLimits | null, usage: (l: AiLimits) => AiLimitUsage,
  text: (left: number, limit: number) => string): string | null {
  if (!limits || limits.unlimited) return null;
  const u = usage(limits);
  const hint = text(u.left, u.limit);
  return u.left === 0 && u.resetsAt ? `${hint} ${t().limits.again(formatWhen(u.resetsAt))}` : hint;
}

/**
 * What the user may still start by clicking (ADR-0021). The server enforces it; the UI only shows it, so the buttons
 * can say "1 of 3 left" instead of failing. Loaded by the pages with AI buttons, refreshed after every AI action.
 */
@Injectable({ providedIn: 'root' })
export class LimitsService {
  private readonly http = inject(HttpClient);
  private readonly current = signal<AiLimits | null>(null);

  readonly limits = this.current.asReadonly();

  async refresh(): Promise<void> {
    try {
      this.current.set(await firstValueFrom(this.http.get<AiLimits>('/api/me/limits')));
    } catch {
      this.current.set(null);                 // only hints – the buttons work without them, the API decides
    }
  }
}
