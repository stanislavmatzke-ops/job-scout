import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AdminPage } from './admin.page';
import { AuthService } from '../../core/auth.service';
import { AdminUser, FeedbackRow, Invitation } from '../../core/admin.models';

function user(overrides: Partial<AdminUser>): AdminUser {
  return {
    id: 'u1', provider: 'google', displayName: 'Jana', email: 'jana@example.com', access: 'Approved',
    createdAt: '2026-10-01T06:00:00Z', lastSignInAt: '2026-10-10T06:00:00Z', accessRequestedAt: null,
    accessRequestMessage: null, suspendedReason: null, hasProfile: true, costLast7DaysUsd: 0, costLast30DaysUsd: 0,
    ...overrides,
  };
}

/** 6e – what the admin sees and what the buttons call. */
describe('AdminPage', () => {
  let http: HttpTestingController;

  async function render(users: AdminUser[], invitations: Invitation[] = [], feedback: FeedbackRow[] = []) {
    TestBed.configureTestingModule({
      imports: [AdminPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            me: signal({ id: 'admin', access: 'Approved', isAdmin: true }),
            providers: () => Promise.resolve({ external: ['google'], devSignIn: true }),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AdminPage);
    fixture.detectChanges();
    http.expectOne('/api/admin/users').flush(users);
    http.expectOne('/api/admin/invitations').flush(invitations);
    http.expectOne('/api/admin/feedback').flush(feedback);
    await settle(fixture);
    return fixture;
  }

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;
  const buttons = (root: HTMLElement, text: string) =>
    Array.from(root.querySelectorAll('button')).filter(b => b.textContent?.trim() === text);

  afterEach(() => http.verify());

  it('shows a waiting request with its message on top', async () => {
    const fixture = await render([
      user({ id: 'p1', displayName: 'Petr', access: 'Pending', accessRequestedAt: '2026-10-10T07:00:00Z',
        accessRequestMessage: 'Hledám práci v HR.' }),
      user({}),
    ]);

    const request = el(fixture).querySelector('.request')!;
    expect(request.textContent).toContain('Petr');
    expect(request.textContent).toContain('Hledám práci v HR.');
    expect(el(fixture).querySelectorAll('.user').length).toBe(1);   // Petr is not repeated among the users
  });

  it('shows costs per user and in total', async () => {
    const fixture = await render([
      user({ id: 'a', costLast7DaysUsd: 0.025, costLast30DaysUsd: 0.1 }),
      user({ id: 'b', costLast7DaysUsd: 0.5, costLast30DaysUsd: 1.4 }),
    ]);

    expect(el(fixture).querySelector('.totals')!.textContent).toContain('$1.50');
    expect(el(fixture).textContent).toContain('$0.025');
  });

  it('the admin cannot suspend their own account', async () => {
    const fixture = await render([user({ id: 'admin', displayName: 'Stanislav' }), user({ id: 'u2' })]);

    expect(buttons(el(fixture), 'Pozastavit').length).toBe(1);
  });

  it('a suspended user shows the reason and can get access back', async () => {
    const fixture = await render([user({ access: 'Suspended', suspendedReason: 'Z kapacitních důvodů.' })]);

    expect(el(fixture).textContent).toContain('Důvod: Z kapacitních důvodů.');
    expect(buttons(el(fixture), 'Obnovit přístup').length).toBe(1);
  });

  it('shows the feedback with a reply link for those who want an answer (13 Oct)', async () => {
    const fixture = await render([], [], [
      { id: 'f1', userId: 'u1', userName: 'Jana', email: 'jana@example.com', kind: 'Problem', rating: 2,
        message: 'Tlačítko nereaguje.', replyWanted: true, createdAt: '2026-10-13T08:00:00Z' },
      { id: 'f2', userId: 'u2', userName: 'Petr', email: null, kind: 'Praise', rating: null,
        message: 'Super!', replyWanted: false, createdAt: '2026-10-12T08:00:00Z' },
    ]);

    const section = el(fixture).querySelector('.feedback')!;
    expect(section.textContent).toContain('Něco nefunguje');
    expect(section.textContent).toContain('Tlačítko nereaguje.');
    expect(section.textContent).toContain('★★☆☆☆');
    const replies = section.querySelectorAll('a.reply');
    expect(replies.length).toBe(1);
    expect(replies[0].getAttribute('href')).toContain('mailto:jana@example.com');
  });

  it('creates an invitation and shows its link once', async () => {
    const fixture = await render([]);
    const input = el(fixture).querySelector('input[name="note"]') as HTMLInputElement;
    input.value = 'Petr z HR';
    input.dispatchEvent(new Event('input'));
    await settle(fixture);

    buttons(el(fixture), 'Vytvořit pozvánku')[0].click();
    await settle(fixture);
    const create = http.expectOne(r => r.method === 'POST' && r.url === '/api/admin/invitations');
    expect(create.request.body).toEqual({ note: 'Petr z HR' });
    create.flush({ id: 'i1', code: 'secret-code', expiresAt: '2026-10-24T06:00:00Z' });
    await settle(fixture);
    http.expectOne(r => r.method === 'GET' && r.url === '/api/admin/invitations').flush([
      { id: 'i1', note: 'Petr z HR', createdAt: '2026-10-10T06:00:00Z', expiresAt: '2026-10-24T06:00:00Z',
        usedAt: null, usedBy: null, isUsable: true },
    ]);
    await settle(fixture);

    const created = el(fixture).querySelector('.created')!;
    expect(created.textContent).toContain('/api/auth/login/google?invite=secret-code');
    expect(created.textContent).toContain('později už ho nezobrazíme');
    expect(el(fixture).querySelector('.invitations')!.textContent).toContain('Petr z HR');
  });

  it('a used invitation says who used it', async () => {
    const fixture = await render([], [
      { id: 'i1', note: 'Petr z HR', createdAt: '2026-10-01T06:00:00Z', expiresAt: '2026-10-15T06:00:00Z',
        usedAt: '2026-10-02T06:00:00Z', usedBy: 'Petr Novák', isUsable: false },
    ]);

    expect(el(fixture).querySelector('.invitations')!.textContent).toContain('použil(a) Petr Novák');
  });
});
