import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ProfileService, cvProblem } from './profile.service';

describe('ProfileService', () => {
  let service: ProfileService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ProfileService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uploads the CV as multipart form data in the field "file"', async () => {
    const file = new File(['# Jana'], 'zivotopis.md', { type: 'text/markdown' });

    const uploading = service.uploadCv(file);
    const req = http.expectOne('/api/profile/cv');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).get('file')).toBeTruthy();
    expect(req.request.headers.has('Content-Type')).toBe(false);   // the browser sets it with the boundary
    req.flush({ version: 1 });

    expect((await uploading).version).toBe(1);
  });

  it('saves notes and gender', async () => {
    const saving = service.updateNotes('Chci full remote.');
    const notes = http.expectOne('/api/profile/notes');
    expect(notes.request.body).toEqual({ notes: 'Chci full remote.' });
    notes.flush({ version: 2 });
    await saving;

    const gender = service.updateGender('Feminine');
    const settings = http.expectOne('/api/profile/settings');
    expect(settings.request.body).toEqual({ gender: 'Feminine' });
    settings.flush(null, { status: 204, statusText: 'No Content' });
    await gender;
  });
});

describe('cvProblem', () => {
  const file = (name: string, size: number) => new File([new Uint8Array(size)], name);

  it('accepts PDF, Word, Markdown and text, whatever the letter case', () => {
    expect(cvProblem(file('CV.PDF', 10))).toBeNull();
    expect(cvProblem(file('Životopis.DOCX', 10))).toBeNull();
    expect(cvProblem(file('cv.md', 10))).toBeNull();
    expect(cvProblem(file('cv.txt', 10))).toBeNull();
  });

  it('rejects other formats, empty and too big files before uploading', () => {
    expect(cvProblem(file('cv.odt', 10))).toContain('PDF');
    expect(cvProblem(file('cv.doc', 10))).toContain('.docx');            // the old Word: say what to do
    expect(cvProblem(file('cv.pdf', 0))).toContain('prázdný');
    expect(cvProblem(file('cv.pdf', 5 * 1024 * 1024 + 1))).toContain('5 MB');
  });
});
