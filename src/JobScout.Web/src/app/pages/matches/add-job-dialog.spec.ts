import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { AddJobDialog } from './add-job-dialog';

/** "Přidat nabídku" (ADR-0013): what the dialog sends, and what it says when it goes wrong. */
describe('AddJobDialog', () => {
  let http: HttpTestingController;
  let closedWith: unknown;

  beforeEach(() => {
    closedWith = undefined;
    TestBed.configureTestingModule({
      imports: [AddJobDialog],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: (value: unknown) => (closedWith = value), disableClose: false } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(AddJobDialog);
    fixture.detectChanges();
    http.expectOne('/api/me/limits').flush(null);           // hints only – the dialog works without them
    await fixture.whenStable();
    return fixture;
  }

  type Inner = { text: { set(v: string): void }; status: { set(v: string): void }; submit(): Promise<void> };

  it('sends the pasted text with the status and closes with the new job', async () => {
    const fixture = await render();
    const dialog = fixture.componentInstance as unknown as Inner;
    dialog.text.set('Hledáme stavbyvedoucího.');
    dialog.status.set('Applied');

    const done = dialog.submit();
    const req = http.expectOne('/api/matches/manual');
    const form = req.request.body as FormData;
    expect(form.get('text')).toBe('Hledáme stavbyvedoucího.');
    expect(form.get('status')).toBe('Applied');
    expect(form.get('evaluate')).toBe('true');
    expect(form.has('url')).toBe(false);
    req.flush({ id: 'new', evaluated: true, alreadyInList: false });
    await done;
    http.expectOne('/api/me/limits').flush(null);

    expect(closedWith).toEqual({ id: 'new', evaluated: true, alreadyInList: false, evaluateRequested: true });
  });

  it('says why the material was refused and stays open', async () => {
    const fixture = await render();
    const dialog = fixture.componentInstance as unknown as Inner;
    dialog.text.set('Faktura');

    const done = dialog.submit();
    http.expectOne('/api/matches/manual').flush(
      { detail: 'V podkladech jsme nenašli nabídku práce. Zkontrolujte prosím, co jste vložili.', reason: 'NotAJobAd' },
      { status: 422, statusText: 'Unprocessable Entity' });
    await done;
    http.expectOne('/api/me/limits').flush(null);
    fixture.detectChanges();

    expect(closedWith).toBeUndefined();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('nenašli nabídku práce');
  });

  it('the add button waits for something to add', async () => {
    const fixture = await render();
    const add = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(b => b.textContent?.includes('Přidat'))!;

    expect(add.disabled).toBe(true);
  });
});
