import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ProfilePage } from './profile.page';
import { AuthService } from '../../core/auth.service';
import { ProfileView } from '../../core/profile.models';

const empty: ProfileView = {
  version: null, cvFileName: null, notes: null, analyzedAt: null, profile: null,
  gender: 'Unspecified', hasCv: false, outdatedEvaluations: 0,
};

describe('ProfilePage', () => {
  let http: HttpTestingController;
  let current: { detectChanges(): void; whenStable(): Promise<unknown> };

  async function render(view: ProfileView) {
    TestBed.configureTestingModule({
      imports: [ProfilePage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { me: signal({ access: 'Approved' }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProfilePage);
    current = fixture;
    fixture.detectChanges();
    http.expectOne('/api/profile').flush(view);
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
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
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'cv.docx')] });

    input.dispatchEvent(new Event('change'));
    await new Promise(resolve => setTimeout(resolve));
    TestBed.inject(HttpTestingController).expectNone('/api/profile/cv');
  });

  it('after the very first CV says that the first search runs right now (6h)', async () => {
    const page = await render(empty);
    const input = page.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['# Jana'], 'cv.md')] });

    input.dispatchEvent(new Event('change'));
    await new Promise(resolve => setTimeout(resolve));
    http.expectOne('/api/profile/cv').flush({ ...empty, hasCv: true, version: 1, cvFileName: 'cv.md' });
    await new Promise(resolve => setTimeout(resolve));
    await current.whenStable();
    current.detectChanges();

    expect(page.textContent).toContain('právě pro vás hledáme první nabídky');
  });
});
