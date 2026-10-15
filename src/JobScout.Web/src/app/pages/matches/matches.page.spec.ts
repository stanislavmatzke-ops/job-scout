import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatchesPage } from './matches.page';
import { MatchList } from '../../core/matches.models';
import { ScoutStatus } from '../../core/scout.service';
import { ConfirmService } from '../../shared/confirm-dialog';
import { dayKey } from '../../core/match-days';

const list: MatchList = {
  view: 'Open',
  counts: { open: 2, inProgress: 1, closed: 0, triagedOut: 7 },
  items: [
    {
      id: 'a', title: 'Solution Architect', company: 'CloudNest s.r.o.', location: 'Praha', url: 'https://www.jobs.cz/rpd/1/',
      stage: 'Evaluated', status: 'New', score: 8, triageScore: 4, summary: 'Sedí role i obor.', triageReason: null,
      hasCoverLetter: true, foundAt: '2026-10-10T06:00:00Z', updatedAt: '2026-10-10T06:00:00Z', evaluationOutdated: true,
    },
    {
      id: 'b', title: 'Tech Lead', company: 'Acme', location: 'Brno', url: 'https://www.jobs.cz/rpd/2/',
      stage: 'Shortlisted', status: 'New', score: null, triageScore: 2, summary: null, triageReason: null,
      hasCoverLetter: false, foundAt: '2026-10-10T06:00:00Z', updatedAt: '2026-10-10T06:00:00Z', evaluationOutdated: false,
    },
  ],
};

/** The list page asks the API for the tab from the URL and shows jobs, scores and tab counts. */
describe('MatchesPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MatchesPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { ask: async () => true } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(view?: string, response: MatchList = list,
                        scout: ScoutStatus = { searching: false, lastSearchAt: '2026-10-10T05:30:00Z' }) {
    const fixture = TestBed.createComponent(MatchesPage);
    if (view) fixture.componentRef.setInput('view', view);
    fixture.detectChanges();
    const req = http.expectOne(r => r.url === '/api/matches');
    req.flush(response);
    http.expectOne('/api/scout/status').flush(scout);
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, text: (fixture.nativeElement as HTMLElement).textContent ?? '', req };
  }

  it('shows the jobs with score, summary and waiting ones', async () => {
    const { text } = await render();

    expect(text).toContain('Solution Architect');
    expect(text).toContain('CloudNest s.r.o.');
    expect(text).toContain('Sedí role i obor.');
    expect(text).toContain('Tech Lead');
    expect(text).toContain('Čeká na ohodnocení');
    expect(text).toContain('starší profil');
  });

  it('a brand new account is sent to the profile page', async () => {
    const empty = { view: 'Open' as const, items: [], counts: { open: 0, inProgress: 0, closed: 0, triagedOut: 0 } };
    const { fixture } = await render(undefined, empty);

    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/profile"]')).toBeTruthy();
  });

  it('shows the counts in the tabs', async () => {
    const { fixture } = await render();

    const tabs = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[mat-tab-link]')).map(t => t.textContent?.trim());
    expect(tabs[0]).toContain('2');
    expect(tabs[3]).toContain('7');
  });

  it('asks for the tab from the URL', async () => {
    const { req } = await render('triagedout', { ...list, view: 'TriagedOut', items: [] });

    expect(req.request.params.get('view')).toBe('TriagedOut');
  });

  it('explains an empty tab', async () => {
    const { text } = await render('closed', { ...list, view: 'Closed', items: [] });

    expect(text).toContain('Žádné uzavřené nabídky');
  });

  // ---------- 6h: the first search runs in the background ----------

  const empty = { view: 'Open' as const, items: [], counts: { open: 0, inProgress: 0, closed: 0, triagedOut: 0 } };

  it('says that jobs are searched for every morning, and when the last search was', async () => {
    const { fixture } = await render();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('hledáme každé ráno');
    expect(text).toContain('Naposledy');
  });

  it('while the first search runs, says so and reloads the list when it ends', async () => {
    vi.useFakeTimers();
    try {
      const fixture = TestBed.createComponent(MatchesPage);
      fixture.detectChanges();
      http.expectOne(r => r.url === '/api/matches').flush(empty);
      http.expectOne('/api/scout/status').flush({ searching: true, lastSearchAt: null });
      await vi.advanceTimersByTimeAsync(0);
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hledáme pro vás nabídky');

      await vi.advanceTimersByTimeAsync(15_000);
      http.expectOne('/api/scout/status').flush({ searching: false, lastSearchAt: '2026-10-10T16:10:00Z' });
      await vi.advanceTimersByTimeAsync(0);
      http.expectOne(r => r.url === '/api/matches').flush(list);       // the new jobs
      await vi.advanceTimersByTimeAsync(0);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).not.toContain('Hledáme pro vás nabídky');
      expect(text).toContain('Solution Architect');
    } finally {
      vi.useRealTimers();
    }
  });

  // ---------- bulk action ----------

  it('changes the status of the selected jobs at once and reloads the tab', async () => {
    const { fixture } = await render();
    const host = fixture.nativeElement as HTMLElement;

    const boxes = host.querySelectorAll<HTMLInputElement>('.item input[type="checkbox"]');
    boxes[0].click();
    boxes[1].click();
    fixture.detectChanges();
    expect(host.textContent).toContain('Vybrány 2 nabídky');

    (fixture.componentInstance as unknown as { bulkStatus: { set(v: string): void } }).bulkStatus.set('Rejected');
    fixture.detectChanges();
    const apply = Array.from(host.querySelectorAll('button')).find(b => b.textContent?.includes('Změnit stav'))!;
    apply.click();
    await new Promise(resolve => setTimeout(resolve));

    const req = http.expectOne('/api/matches/status');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ ids: ['a', 'b'], status: 'Rejected', note: null });
    req.flush({ changed: 2 });
    await new Promise(resolve => setTimeout(resolve));

    http.expectOne(r => r.url === '/api/matches').flush({ ...list, items: [] });
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();
    expect(host.textContent).not.toContain('Vybrány');
  });

  it('select all picks every job in the tab', async () => {
    const { fixture } = await render();
    const host = fixture.nativeElement as HTMLElement;

    host.querySelector<HTMLInputElement>('.bulk input[type="checkbox"]')!.click();
    fixture.detectChanges();

    expect(host.textContent).toContain('Vybrány 2 nabídky');
  });

  // ---------- jobs of one day, grouped by day ----------

  const twoDays: MatchList = {
    ...list,
    items: [
      { ...list.items[0], id: 'old', title: 'Starší nabídka', foundAt: '2026-10-08T08:00:00Z' },
      { ...list.items[0], id: 'new', title: 'Novější nabídka', foundAt: '2026-10-10T08:00:00Z', score: 6 },
    ],
  };

  async function renderWith(params: Record<string, string>) {
    const fixture = TestBed.createComponent(MatchesPage);
    for (const [name, value] of Object.entries(params)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/matches').flush(twoDays);
    http.expectOne('/api/scout/status').flush({ searching: false, lastSearchAt: null });
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement);
  }

  it('shows only the jobs of the day from the URL', async () => {
    const host = await renderWith({ day: dayKey(new Date('2026-10-10T08:00:00Z')) });

    expect(host.textContent).toContain('Novější nabídka');
    expect(host.textContent).not.toContain('Starší nabídka');
    expect(host.textContent).toContain('Zobrazeno 1 z 2.');
  });

  it('groups the jobs by day with a heading, newest first', async () => {
    const host = await renderWith({ sort: 'day' });

    const headings = Array.from(host.querySelectorAll('.day-heading')).map(h => h.textContent ?? '');
    expect(headings.length).toBe(2);
    expect(headings[0]).toContain('10. 10.');
    const titles = Array.from(host.querySelectorAll('.item .title')).map(a => a.textContent);
    expect(titles).toEqual(['Novější nabídka', 'Starší nabídka']);
  });

  it('a day without jobs says so', async () => {
    const host = await renderWith({ day: '2026-01-01' });

    expect(host.textContent).toContain('V tento den jsme v této záložce žádné nabídky nenašli.');
  });
});
