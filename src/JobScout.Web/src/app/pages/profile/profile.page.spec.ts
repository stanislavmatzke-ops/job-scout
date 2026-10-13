import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ProfilePage } from './profile.page';
import { AuthService } from '../../core/auth.service';
import { LimitsService } from '../../core/limits.service';
import { ProfileView } from '../../core/profile.models';

const empty: ProfileView = {
  version: null, cvFileName: null, notes: null, analyzedAt: null, profile: null,
  gender: 'Unspecified', hasCv: false, outdatedEvaluations: 0,
};

describe('ProfilePage', () => {
  let http: HttpTestingController;
  let current: { detectChanges(): void; whenStable(): Promise<unknown> };

  const searched = { searching: false, lastSearchAt: '2026-10-12T05:30:00Z' };
  const neverSearched = { searching: false, lastSearchAt: null };

  async function settle() {
    await new Promise(resolve => setTimeout(resolve));
    await current.whenStable();
    current.detectChanges();
  }

  async function render(view: ProfileView, scout: object = searched) {
    TestBed.configureTestingModule({
      imports: [ProfilePage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        // AI limits (ADR-0021) are only hints – a stub keeps these tests about the page itself
        { provide: LimitsService, useValue: { limits: signal(null), refresh: () => Promise.resolve() } },
        { provide: AuthService, useValue: { me: signal({ access: 'Approved' }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProfilePage);
    current = fixture;
    fixture.detectChanges();
    http.expectOne('/api/profile').flush(view);
    await new Promise(resolve => setTimeout(resolve));
    http.expectOne('/api/scout/status').flush(scout);
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => http.verify());

  it('a new user is guided to upload the CV first', async () => {
    const page = await render(empty);

    expect(page.textContent).toContain('Začněte nahráním životopisu');
    expect(page.textContent).toContain('nejprve ho prosím nahrajte');
  });

  it('tells how many open jobs were evaluated with an older profile', async () => {
    const page = await render({ ...empty, hasCv: true, version: 3, cvFileName: 'cv.pdf', outdatedEvaluations: 12 });

    expect(page.textContent).toContain('starší verze profilu');
    expect(page.textContent).toContain('12');
  });

  it('rejects an unsupported file without uploading it', async () => {
    const page = await render(empty);
    const input = page.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'cv.doc')] });

    input.dispatchEvent(new Event('change'));
    await new Promise(resolve => setTimeout(resolve));
    TestBed.inject(HttpTestingController).expectNone('/api/profile/cv');
  });

  it('after the first CV the search waits for the user – notes first, then the first search (12 Oct)', async () => {
    const page = await render(empty, neverSearched);
    const input = page.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['# Jana'], 'cv.md')] });

    input.dispatchEvent(new Event('change'));
    await new Promise(resolve => setTimeout(resolve));
    http.expectOne('/api/profile/cv').flush({ ...empty, hasCv: true, version: 1, cvFileName: 'cv.md' });
    await new Promise(resolve => setTimeout(resolve));
    http.expectOne('/api/scout/status').flush(neverSearched);
    await settle();

    expect(page.textContent).toContain('Poslední krok: spusťte první hledání');
    expect(page.textContent).not.toContain('právě pro vás hledáme první nabídky');
  });

  it('starting the first search calls the API and says it runs', async () => {
    const page = await render({ ...empty, hasCv: true, version: 1, cvFileName: 'cv.md' }, neverSearched);
    const start = Array.from(page.querySelectorAll('button')).find(b => b.textContent?.includes('Spustit první hledání'))!;

    start.click();
    await new Promise(resolve => setTimeout(resolve));
    const req = http.expectOne('/api/scout/first');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 202, statusText: 'Accepted' });
    await settle();

    expect(page.textContent).toContain('právě pro vás hledáme první nabídky');
    expect(page.textContent).not.toContain('Poslední krok');
  });

  it('who has been searched for gets no start button', async () => {
    const page = await render({ ...empty, hasCv: true, version: 2, cvFileName: 'cv.md' }, searched);

    expect(page.textContent).not.toContain('Spustit první hledání');
  });
});
