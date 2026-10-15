import { MatchListItem } from './matches.models';
import { arrangeByDay, dayHeading, dayKey, dayLabel, parseDay, parseSort } from './match-days';

function job(id: string, foundAt: Date, score: number | null, triageScore: number | null = null): MatchListItem {
  return {
    id, title: id, company: 'Firma', location: 'Praha', url: '', stage: score === null ? 'Shortlisted' : 'Evaluated',
    status: 'New', score, triageScore, summary: null, triageReason: null, hasCoverLetter: false,
    foundAt: foundAt.toISOString(), updatedAt: foundAt.toISOString(), evaluationOutdated: false,
  };
}

// Local times – the day is the user's day, whatever the test machine's time zone
const oct15 = (h: number) => new Date(2026, 9, 15, h);
const oct14 = (h: number) => new Date(2026, 9, 14, h);

/** "Nabídky z jednoho dne" and "po dnech": the user's own calendar day, newest first, the best within a day first. */
describe('match days', () => {
  const items = [job('a', oct14(6), 9), job('b', oct15(6), 5), job('c', oct15(7), 8), job('d', oct15(6), null, 4)];

  it('keeps the API order when not grouping, only filtering the day', () => {
    expect(arrangeByDay(items, null, 'default')[0].items.map(i => i.id)).toEqual(['a', 'b', 'c', 'd']);
    expect(arrangeByDay(items, '2026-10-15', 'default')[0].items.map(i => i.id)).toEqual(['b', 'c', 'd']);
  });

  it('groups by day, newest day first, best score first within a day, waiting ones last', () => {
    const groups = arrangeByDay(items, null, 'day');

    expect(groups.map(g => g.key)).toEqual(['2026-10-15', '2026-10-14']);
    expect(groups[0].items.map(i => i.id)).toEqual(['c', 'b', 'd']);
  });

  it('a day without jobs gives nothing to show', () => {
    expect(arrangeByDay(items, '2026-10-01', 'default')).toEqual([]);
    expect(arrangeByDay(items, '2026-10-01', 'day')).toEqual([]);
  });

  it('accepts only real dates from the URL', () => {
    expect(parseDay('2026-10-15')).toBe('2026-10-15');
    expect(parseDay('2026-02-30')).toBeNull();
    expect(parseDay('15.10.2026')).toBeNull();
    expect(parseDay('<script>')).toBeNull();
    expect(parseDay(undefined)).toBeNull();
    expect(parseSort('day')).toBe('day');
    expect(parseSort('anything')).toBe('default');
  });

  it('says today and yesterday, otherwise the date', () => {
    const today = oct15(12);

    expect(dayLabel('2026-10-15', today)).toBe('dnes');
    expect(dayLabel('2026-10-14', today)).toBe('včera');
    expect(dayLabel('2026-10-12', today)).toBe('12. 10.');
    expect(dayHeading('2026-10-15', today)).toBe('Dnes');
    expect(dayHeading('2026-10-12', today)).toBe('pondělí 12. 10.');
    expect(dayKey(oct15(0))).toBe('2026-10-15');
  });
});
