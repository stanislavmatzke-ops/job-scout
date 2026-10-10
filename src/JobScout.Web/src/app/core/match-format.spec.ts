import { HttpErrorResponse } from '@angular/common/http';
import { STATUS_OPTIONS, VIEWS, parseView, problemMessage, scoreTone, statusLabel } from './match-format';

describe('match-format', () => {
  it('reads the tab from the URL, case-insensitive, Open by default', () => {
    expect(parseView('inprogress')).toBe('InProgress');
    expect(parseView('TriagedOut')).toBe('TriagedOut');
    expect(parseView(undefined)).toBe('Open');
    expect(parseView('nonsense')).toBe('Open');
  });

  it('has a tab for every view of the API', () => {
    expect(VIEWS.map(v => v.view)).toEqual(['Open', 'InProgress', 'Closed', 'TriagedOut']);
  });

  it('colours the score: 7+ worth a reply, 4–6 maybe, up to 3 weak, none = waiting', () => {
    expect(scoreTone(9)).toBe('good');
    expect(scoreTone(7)).toBe('good');
    expect(scoreTone(6)).toBe('maybe');
    expect(scoreTone(4)).toBe('maybe');
    expect(scoreTone(3)).toBe('weak');
    expect(scoreTone(null)).toBe('pending');
  });

  it('offers every status of the domain, in the order of a job hunt', () => {
    expect(STATUS_OPTIONS.map(s => s.value)).toEqual([
      'New', 'SavedForLater', 'Applied', 'Call', 'Round1', 'Round2', 'Offer', 'Rejected', 'Withdrawn', 'Dismissed', 'Hidden',
    ]);
    expect(statusLabel('Applied')).toBe('Reakce odeslána');
  });

  it('shows the API problem detail, or a fallback', () => {
    const problem = new HttpErrorResponse({ status: 409, error: { title: 'Conflict', detail: 'Nejprve nahrajte své CV.' } });
    expect(problemMessage(problem, 'Nepodařilo se.')).toBe('Nejprve nahrajte své CV.');
    expect(problemMessage(new HttpErrorResponse({ status: 500 }), 'Nepodařilo se.')).toBe('Nepodařilo se.');
    expect(problemMessage(new Error('x'), 'Nepodařilo se.')).toBe('Nepodařilo se.');
  });
});
