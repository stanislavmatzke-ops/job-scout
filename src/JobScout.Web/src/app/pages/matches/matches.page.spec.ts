import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatchesPage } from './matches.page';
import { MatchList } from '../../core/matches.models';

const list: MatchList = {
  view: 'Open',
  counts: { open: 2, inProgress: 1, closed: 0, triagedOut: 7 },
  items: [
    {
      id: 'a', title: 'Solution Architect', company: 'CloudNest s.r.o.', location: 'Praha', url: 'https://www.jobs.cz/rpd/1/',
      stage: 'Evaluated', status: 'New', score: 8, triageScore: 4, summary: 'Sedí role i obor.', triageReason: null,
      hasCoverLetter: true, foundAt: '2026-10-10T06:00:00Z', updatedAt: '2026-10-10T06:00:00Z',
    },
    {
      id: 'b', title: 'Tech Lead', company: 'Acme', location: 'Brno', url: 'https://www.jobs.cz/rpd/2/',
      stage: 'Shortlisted', status: 'New', score: null, triageScore: 2, summary: null, triageReason: null,
      hasCoverLetter: false, foundAt: '2026-10-10T06:00:00Z', updatedAt: '2026-10-10T06:00:00Z',
    },
  ],
};

/** The list page asks the API for the tab from the URL and shows jobs, scores and tab counts. */
describe('MatchesPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MatchesPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(view?: string, response: MatchList = list) {
    const fixture = TestBed.createComponent(MatchesPage);
    if (view) fixture.componentRef.setInput('view', view);
    fixture.detectChanges();
    const req = http.expectOne(r => r.url === '/api/matches');
    req.flush(response);
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
});
