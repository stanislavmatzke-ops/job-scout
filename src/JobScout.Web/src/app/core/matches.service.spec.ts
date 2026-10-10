import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatchesService } from './matches.service';

/** The client side of /api/matches (5b) – URLs, methods and bodies the API expects. */
describe('MatchesService', () => {
  let service: MatchesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(MatchesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists one tab', async () => {
    const loading = service.list('InProgress');
    const req = http.expectOne(r => r.url === '/api/matches');
    expect(req.request.params.get('view')).toBe('InProgress');
    req.flush({ view: 'InProgress', items: [], counts: { open: 0, inProgress: 0, closed: 0, triagedOut: 0 } });

    expect((await loading).view).toBe('InProgress');
  });

  it('loads the detail', async () => {
    const loading = service.detail('abc');
    http.expectOne('/api/matches/abc').flush({ id: 'abc' });

    expect((await loading).id).toBe('abc');
  });

  it('changes the status with a note', async () => {
    const saving = service.changeStatus('abc', 'Applied', 'Odesláno přes jobs.cz');
    const req = http.expectOne('/api/matches/abc/status');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ status: 'Applied', note: 'Odesláno přes jobs.cz' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    await saving;
  });

  it('saves notes', async () => {
    const saving = service.updateNotes('abc', 'Zeptat se na HO');
    const req = http.expectOne('/api/matches/abc/notes');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ notes: 'Zeptat se na HO' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    await saving;
  });

  // ---------- on demand (6c) ----------

  it('evaluates and returns the fresh detail', async () => {
    const evaluating = service.evaluate('abc');
    const req = http.expectOne('/api/matches/abc/evaluate');
    expect(req.request.method).toBe('POST');
    req.flush({ id: 'abc', score: 6 });

    expect((await evaluating).score).toBe(6);
  });

  it('writes a letter – replacing only when asked', async () => {
    const writing = service.writeLetter('abc', true);
    const req = http.expectOne(r => r.url === '/api/matches/abc/letter');
    expect(req.request.method).toBe('POST');
    expect(req.request.params.get('replace')).toBe('true');
    req.flush({ id: 'abc', coverLetter: 'Dobrý den, …' });

    expect((await writing).coverLetter).toBe('Dobrý den, …');
  });

  it('saves the edited letter', async () => {
    const saving = service.updateLetter('abc', 'Dobrý den, upraveno.');
    const req = http.expectOne('/api/matches/abc/letter');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ text: 'Dobrý den, upraveno.' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    await saving;
  });
});
