import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatchDetailPage } from './match-detail.page';
import { AuthService } from '../../core/auth.service';
import { LimitsService } from '../../core/limits.service';
import { AccessStatus } from '../../core/models';
import { MatchDetail } from '../../core/matches.models';

function job(overrides: Partial<MatchDetail> = {}): MatchDetail {
  return {
    id: 'm1', title: 'Solution Architect', company: 'CloudNest s.r.o.', location: 'Praha', url: 'https://www.jobs.cz/rpd/1/',
    source: 'jobs.cz', stage: 'Shortlisted', status: 'New', score: null, triageScore: 2, triageReason: null,
    evaluation: null, coverLetter: null, notes: null, adText: null, adTextIncomplete: false,
    foundAt: '2026-10-10T06:00:00Z', updatedAt: '2026-10-10T06:00:00Z', history: [], evaluationOutdated: false,
    ...overrides,
  };
}

const evaluated = (overrides: Partial<MatchDetail> = {}) => job({
  stage: 'Evaluated', score: 8,
  evaluation: {
    score: 8, summary: 'Sedí role i obor.', reasons: ['Architektura'], risks: [], questions: [],
    salary: '', remoteWork: 'Hybrid', employmentType: 'Employee',
  },
  ...overrides,
});

/** 6c – the AI buttons on the detail: what they call, what they show, and that a suspended account can't use them. */
describe('MatchDetailPage – AI on demand', () => {
  let http: HttpTestingController;

  async function render(detail: MatchDetail, access: AccessStatus = 'Approved') {
    TestBed.configureTestingModule({
      imports: [MatchDetailPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        // AI limits (ADR-0021) are only hints – a stub keeps these tests about the page itself
        { provide: LimitsService, useValue: { limits: signal(null), refresh: () => Promise.resolve() } },
        { provide: AuthService, useValue: { me: signal({ access }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MatchDetailPage);
    fixture.componentRef.setInput('id', 'm1');
    fixture.detectChanges();
    http.expectOne('/api/matches/m1').flush(detail);
    await settle(fixture);
    return fixture;
  }

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;
  const button = (root: HTMLElement, text: string) =>
    Array.from(root.querySelectorAll('button')).find(b => b.textContent?.includes(text)) as HTMLButtonElement | undefined;

  afterEach(() => http.verify());

  it('evaluates a waiting job and shows the result', async () => {
    const fixture = await render(job());

    button(el(fixture), 'Ohodnotit')!.click();
    http.expectOne('/api/matches/m1/evaluate').flush(evaluated());
    await settle(fixture);

    expect(el(fixture).textContent).toContain('Sedí role i obor.');
    expect(button(el(fixture), 'Ohodnotit')).toBeUndefined();          // a fresh evaluation is not offered again
  });

  it('offers re-evaluation only for an evaluation made with an older profile', async () => {
    const fixture = await render(evaluated({ evaluationOutdated: true }));

    expect(button(el(fixture), 'Přehodnotit podle aktuálního profilu')).toBeTruthy();
    expect(el(fixture).textContent).toContain('starší verze vašeho profilu');
  });

  it('offers "evaluate anyway" for a triaged-out job', async () => {
    const fixture = await render(job({ stage: 'TriagedOut', triageReason: 'Jiný obor' }));

    expect(button(el(fixture), 'Přesto ohodnotit')).toBeTruthy();
  });

  it('suspended account sees the AI buttons disabled and why', async () => {
    const fixture = await render(evaluated({ evaluationOutdated: true }), 'Suspended');

    expect(button(el(fixture), 'Přehodnotit')!.disabled).toBe(true);
    expect(button(el(fixture), 'Napsat dopis')!.disabled).toBe(true);
    expect(el(fixture).textContent).toContain('pozastaveného účtu');
  });

  it('writes a letter for an evaluated job', async () => {
    const fixture = await render(evaluated());

    button(el(fixture), 'Napsat dopis')!.click();
    const req = http.expectOne(r => r.url === '/api/matches/m1/letter');
    expect(req.request.params.get('replace')).toBe('false');
    req.flush(evaluated({ coverLetter: 'Dobrý den,\n\nreaguji na Vaši nabídku…' }));
    await settle(fixture);

    expect(el(fixture).textContent).toContain('reaguji na Vaši nabídku');
  });

  it('a job that is not evaluated yet offers no letter', async () => {
    const fixture = await render(job());

    expect(button(el(fixture), 'Napsat dopis')).toBeUndefined();
    expect(el(fixture).textContent).toContain('jakmile bude nabídka ohodnocena');
  });

  it('removed ad: the message from the API, then the job is reloaded', async () => {
    const fixture = await render(job());

    button(el(fixture), 'Ohodnotit')!.click();
    http.expectOne('/api/matches/m1/evaluate').flush(
      { detail: 'Inzerát již na portálu není k dispozici.' }, { status: 410, statusText: 'Gone' });
    await settle(fixture);

    http.expectOne('/api/matches/m1').flush(job({ stage: 'TriagedOut', triageReason: 'Inzerát už není dostupný.' }));
    await settle(fixture);
    expect(el(fixture).textContent).toContain('Inzerát už není dostupný.');
  });

  it('the letter can be edited and saved', async () => {
    const fixture = await render(evaluated({ coverLetter: 'Dobrý den, původní.' }));

    button(el(fixture), 'Upravit')!.click();
    await settle(fixture);
    const textarea = el(fixture).querySelector('textarea[name="letter"]') as HTMLTextAreaElement;
    textarea.value = 'Dobrý den, upraveno.';
    textarea.dispatchEvent(new Event('input'));
    await settle(fixture);
    button(el(fixture), 'Uložit dopis')!.click();

    const put = http.expectOne('/api/matches/m1/letter');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ text: 'Dobrý den, upraveno.' });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    http.expectOne('/api/matches/m1').flush(evaluated({ coverLetter: 'Dobrý den, upraveno.' }));
    await settle(fixture);

    expect(el(fixture).textContent).toContain('Dobrý den, upraveno.');
  });
});
