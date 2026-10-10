import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminService, formatUsd, invitationLinks } from './admin.service';

describe('AdminService', () => {
  let service: AdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('approves and suspends with the reason in the body', async () => {
    const approving = service.approve('u1');
    const approve = http.expectOne('/api/admin/users/u1/approve');
    expect(approve.request.method).toBe('POST');
    approve.flush(null, { status: 204, statusText: 'No Content' });
    await approving;

    const suspending = service.suspend('u1', 'Z kapacitních důvodů.');
    const suspend = http.expectOne('/api/admin/users/u1/suspend');
    expect(suspend.request.body).toEqual({ reason: 'Z kapacitních důvodů.' });
    suspend.flush(null, { status: 204, statusText: 'No Content' });
    await suspending;
  });

  it('creates an invitation with a note', async () => {
    const creating = service.createInvitation('Petr z HR');
    const req = http.expectOne('/api/admin/invitations');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ note: 'Petr z HR' });
    req.flush({ id: 'i1', code: 'abc', expiresAt: '2026-10-24T00:00:00Z' });

    expect((await creating).code).toBe('abc');
  });
});

describe('formatUsd', () => {
  it('shows a dash for nothing, cents precisely below a dollar, two decimals above', () => {
    expect(formatUsd(0)).toBe('—');
    expect(formatUsd(0.025)).toBe('$0.025');
    expect(formatUsd(12.5)).toBe('$12.50');
  });
});

describe('invitationLinks', () => {
  it('builds one sign-in link per configured provider on the public origin', () => {
    const links = invitationLinks('a-b_c', 'https://localhost:4200', ['microsoft', 'google']);

    expect(links.map(l => l.label)).toEqual(['Microsoft', 'Google']);
    expect(links[1].url).toBe('https://localhost:4200/api/auth/login/google?invite=a-b_c&returnUrl=%2F');
  });

  it('no external providers = no links', () => {
    expect(invitationLinks('x', 'https://localhost:4200', [])).toEqual([]);
  });
});
