import { HttpErrorResponse } from '@angular/common/http';
import { EmploymentType, MatchCounts, MatchStatus, MatchView, RemoteWork } from './matches.models';

export interface ViewTab { view: MatchView; param: string; label: string; countKey: keyof MatchCounts; empty: string; }

export const VIEWS: ViewTab[] = [
  { view: 'Open', param: 'open', label: 'K rozhodnutí', countKey: 'open',
    empty: 'Žádné nabídky nečekají na vaše rozhodnutí. Nové přibudou po ranním vyhledávání.' },
  { view: 'InProgress', param: 'inprogress', label: 'Rozpracované', countKey: 'inProgress',
    empty: 'Zatím jste na žádnou nabídku nereagovali.' },
  { view: 'Closed', param: 'closed', label: 'Uzavřené', countKey: 'closed',
    empty: 'Žádné uzavřené nabídky.' },
  { view: 'TriagedOut', param: 'triagedout', label: 'Vyřazené tříděním', countKey: 'triagedOut',
    empty: 'Automatické třídění zatím nic nevyřadilo.' },
];

/** ?view=… from the URL → a view; anything unknown = Open. */
export function parseView(param: string | null | undefined): MatchView {
  return VIEWS.find(v => v.param === param?.toLowerCase())?.view ?? 'Open';
}

export const STATUS_OPTIONS: { value: MatchStatus; label: string }[] = [
  { value: 'New', label: 'Nová' },
  { value: 'SavedForLater', label: 'Uloženo na později' },
  { value: 'Applied', label: 'Reakce odeslána' },
  { value: 'Call', label: 'Telefonát / první kontakt' },
  { value: 'Round1', label: '1. kolo pohovoru' },
  { value: 'Round2', label: '2. kolo pohovoru' },
  { value: 'Offer', label: 'Nabídka práce' },
  { value: 'Rejected', label: 'Zamítnuto' },
  { value: 'Withdrawn', label: 'Staženo' },
  { value: 'Dismissed', label: 'Nemám zájem' },
  { value: 'Hidden', label: 'Skrýt nabídku' },
];

export function statusLabel(status: MatchStatus): string {
  return STATUS_OPTIONS.find(s => s.value === status)?.label ?? status;
}

const REMOTE: Record<RemoteWork, string> = {
  Unknown: 'Neuvedeno', Full: 'Plně na dálku', Hybrid: 'Hybridně', Onsite: 'Na pracovišti',
};
const EMPLOYMENT: Record<EmploymentType, string> = {
  Unknown: 'Neuvedeno', Employee: 'Zaměstnanec (HPP)', Contractor: 'Externista (IČO)', Both: 'HPP i IČO',
};
export const remoteLabel = (value: RemoteWork) => REMOTE[value] ?? value;
export const employmentLabel = (value: EmploymentType) => EMPLOYMENT[value] ?? value;

/** 7+ = worth a reply (the same threshold as the automatic letters), 4–6 maybe, ≤3 weak, none = not evaluated yet. */
export function scoreTone(score: number | null): 'good' | 'maybe' | 'weak' | 'pending' {
  if (score === null) return 'pending';
  return score >= 7 ? 'good' : score >= 4 ? 'maybe' : 'weak';
}

/** The API's ProblemDetails.detail is written for users – show it; otherwise a generic message. */
export function problemMessage(error: unknown, fallback: string): string {
  return error instanceof HttpErrorResponse && typeof error.error?.detail === 'string' ? error.error.detail : fallback;
}
