import { formatDate } from '@angular/common';
import { MatchListItem } from './matches.models';
import { lang, t } from './i18n';

/** How the list is ordered: as the API sends it (Open = best score first), or newest day first, best within a day. */
export type MatchSort = 'default' | 'day';

/** One day of the list – a heading and its jobs. */
export interface MatchDay { key: string; items: MatchListItem[]; }

/** "2026-10-15" – the day in the user's own time zone (a job found at 00:30 belongs to that morning, not to UTC). */
export function dayKey(value: string | Date): string {
  const d = typeof value === 'string' ? new Date(value) : value;
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** ?day=… from the URL → a day key, or null for anything that is not a real date. */
export function parseDay(param: string | null | undefined): string | null {
  if (!param || !/^\d{4}-\d{2}-\d{2}$/.test(param)) return null;
  const [y, m, d] = param.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  return date.getFullYear() === y && date.getMonth() === m - 1 && date.getDate() === d ? param : null;
}

/** The key back to a local midnight – for the date picker. */
export function dayDate(key: string): Date {
  const [y, m, d] = key.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function parseSort(param: string | null | undefined): MatchSort {
  return param === 'day' ? 'day' : 'default';
}

/**
 * The jobs to show: only the chosen day (if any), then either one group in the API's order,
 * or one group per day – newest day first, within a day the best score first, waiting ones by triage relevance.
 */
export function arrangeByDay(items: MatchListItem[], day: string | null, sort: MatchSort): MatchDay[] {
  const shown = day ? items.filter(i => dayKey(i.foundAt) === day) : items;
  if (sort === 'default')
    return shown.length ? [{ key: day ?? '', items: shown }] : [];

  const groups = new Map<string, MatchListItem[]>();
  for (const item of shown) {
    const key = dayKey(item.foundAt);
    groups.set(key, [...(groups.get(key) ?? []), item]);
  }
  return [...groups.entries()]
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([key, group]) => ({ key, items: [...group].sort(byScore) }));
}

function byScore(a: MatchListItem, b: MatchListItem): number {
  return (b.score ?? -1) - (a.score ?? -1)
    || (b.triageScore ?? -1) - (a.triageScore ?? -1)
    || b.foundAt.localeCompare(a.foundAt);
}

/** "dnes" / "včera" / "13. 10." – relative to the user's today. */
export function dayLabel(key: string, today: Date = new Date()): string {
  const yesterday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1);
  if (key === dayKey(today)) return t().days.today;
  if (key === dayKey(yesterday)) return t().days.yesterday;
  return formatDate(dayDate(key), t().fmt.dayMonth, lang());
}

/** A heading of a day group: "Dnes", "Včera", "pondělí 13. 10.". */
export function dayHeading(key: string, today: Date = new Date()): string {
  const label = dayLabel(key, today);
  return label === formatDate(dayDate(key), t().fmt.dayMonth, lang())
    ? formatDate(dayDate(key), t().fmt.dayHeading, lang())
    : capitalize(label);
}

const pad = (n: number) => String(n).padStart(2, '0');
const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);
