import { HttpErrorResponse } from '@angular/common/http';
import { EmploymentType, MatchCounts, MatchStatus, MatchView, RemoteWork } from './matches.models';
import { lang, t } from './i18n';
import { limitMessage } from './limits.service';

export interface ViewTab { view: MatchView; param: string; readonly label: string; countKey: keyof MatchCounts; readonly empty: string; }

/** Labels are getters – they follow the current language without the templates doing anything. */
function tab(view: MatchView, param: string, countKey: keyof MatchCounts): ViewTab {
  return {
    view, param, countKey,
    get label() { return t().views[view].label; },
    get empty() { return t().views[view].empty; },
  };
}

export const VIEWS: ViewTab[] = [
  tab('Open', 'open', 'open'),
  tab('Saved', 'saved', 'saved'),
  tab('InProgress', 'inprogress', 'inProgress'),
  tab('Closed', 'closed', 'closed'),
  tab('TriagedOut', 'triagedout', 'triagedOut'),
];

/** ?view=… from the URL → a view; anything unknown = Open. */
export function parseView(param: string | null | undefined): MatchView {
  return VIEWS.find(v => v.param === param?.toLowerCase())?.view ?? 'Open';
}

const STATUSES: MatchStatus[] = [
  'New', 'SavedForLater', 'Applied', 'Call', 'Round1', 'Round2', 'Offer', 'Rejected', 'Withdrawn', 'Dismissed', 'Hidden',
];

export const STATUS_OPTIONS: { readonly value: MatchStatus; readonly label: string }[] =
  STATUSES.map(value => ({ value, get label() { return t().status[value]; } }));

export function statusLabel(status: MatchStatus): string {
  return t().status[status] ?? status;
}

export const remoteLabel = (value: RemoteWork) => t().remote[value] ?? value;
export const employmentLabel = (value: EmploymentType) => t().employment[value] ?? value;

/** 7+ = worth a reply (the same threshold as the automatic letters), 4–6 maybe, ≤3 weak, none = not evaluated yet. */
export function scoreTone(score: number | null): 'good' | 'maybe' | 'weak' | 'pending' {
  if (score === null) return 'pending';
  return score >= 7 ? 'good' : score >= 4 ? 'maybe' : 'weak';
}

/**
 * The API's ProblemDetails.detail is written for users – in Czech. Shown in the Czech UI;
 * the English UI shows our own fallback instead of a Czech sentence.
 */
export function problemMessage(error: unknown, fallback: string): string {
  return lang() === 'cs' && error instanceof HttpErrorResponse && typeof error.error?.detail === 'string'
    ? error.error.detail
    : fallback;
}

/**
 * Errors of the AI buttons. In Czech the API already says what happened (409 no CV / not evaluated, 410 ad removed,
 * 502 letter cut off, 503 portal down); in English we say it by the status code. 403 comes without a body.
 */
export function aiErrorMessage(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  const e = t().errors;
  if (status === 403) return e.aiUnavailable;
  if (status === 429) return limitMessage(error);           // an AI limit (ADR-0021) – worded in the user's language
  const byStatus: Record<number, string> = { 409: e.ai409, 410: e.ai410, 502: e.ai502, 503: e.ai503 };
  return problemMessage(error, byStatus[status] ?? t().common.actionFailed);
}
