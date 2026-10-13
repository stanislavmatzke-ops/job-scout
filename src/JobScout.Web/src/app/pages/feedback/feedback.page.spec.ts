import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { FeedbackPage } from './feedback.page';
import { AuthService } from '../../core/auth.service';

/** "Zpětná vazba" (13 Oct): pick what it is about, write, send – and the reply checkbox only for a real mailbox. */
describe('FeedbackPage', () => {
  let http: HttpTestingController;

  async function render(me: object = { access: 'Approved', email: 'jana@example.com' }) {
    TestBed.configureTestingModule({
      imports: [FeedbackPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { me: signal(me) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(FeedbackPage);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;
  const sendButton = (root: HTMLElement) =>
    Array.from(root.querySelectorAll('button')).find(b => b.textContent?.includes('Odeslat')) as HTMLButtonElement;

  async function write(fixture: { nativeElement: unknown; whenStable(): Promise<unknown>; detectChanges(): void }, text: string) {
    const textarea = el(fixture).querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = text;
    textarea.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  async function pick(fixture: { nativeElement: unknown; whenStable(): Promise<unknown>; detectChanges(): void }, label: string) {
    const chip = Array.from(el(fixture).querySelectorAll('mat-chip-option')).find(c => c.textContent?.includes(label)) as HTMLElement;
    (chip.querySelector('[role="option"]') as HTMLElement ?? chip).click();
    await settle(fixture);
  }

  afterEach(() => http.verify());

  it('can be sent only with a kind and a message', async () => {
    const fixture = await render();
    expect(sendButton(el(fixture)).disabled).toBe(true);

    await write(fixture, 'Přidejte prosím další portály.');
    expect(sendButton(el(fixture)).disabled).toBe(true);           // still no kind

    await pick(fixture, 'Nápad');
    expect(sendButton(el(fixture)).disabled).toBe(false);
  });

  it('sends what was written and thanks', async () => {
    const fixture = await render();
    await pick(fixture, 'Něco nefunguje');
    await write(fixture, '  Tlačítko nereaguje.  ');
    (el(fixture).querySelector('[aria-label="4 z 5"]') as HTMLButtonElement).click();
    await settle(fixture);

    sendButton(el(fixture)).click();
    await settle(fixture);
    const req = http.expectOne('/api/feedback');
    expect(req.request.body).toEqual({ kind: 'Problem', rating: 4, message: 'Tlačítko nereaguje.', replyWanted: false });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);

    expect(el(fixture).textContent).toContain('Děkujeme, zprávu jsme přijali.');
  });

  it('offers a reply only to a real mailbox', async () => {
    const withMail = await render();
    expect(el(withMail).textContent).toContain('Chci odpověď na jana@example.com');

    TestBed.resetTestingModule();
    const demo = await render({ access: 'Approved', email: '', isDemo: true });
    expect(el(demo).textContent).not.toContain('Chci odpověď');
    expect(el(demo).textContent).toContain('napište do zprávy, jak vás můžeme kontaktovat');
  });

  it('too many messages today is said kindly', async () => {
    const fixture = await render();
    await pick(fixture, 'Jiné');
    await write(fixture, 'Ještě jedna věc.');

    sendButton(el(fixture)).click();
    await settle(fixture);
    http.expectOne('/api/feedback').flush({ detail: 'x' }, { status: 429, statusText: 'Too Many Requests' });
    await settle(fixture);

    expect(el(fixture).textContent).toContain('Další můžete poslat zítra');
  });
});
