import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { adminGuard, memberGuard, signedInGuard, signedOutGuard } from './guards';
import { AccessStatus, Me } from './models';

function user(access: AccessStatus, isAdmin = false): Me {
  return {
    id: '01', provider: 'google', displayName: 'Jana', email: '', access, isAdmin,
    accessRequestedAt: null, suspendedReason: null, identity: 'google:g-1',
  };
}

/** Where each kind of user may go (ADR-0017): signed out → login, Pending → request access, admin pages only for admins. */
describe('guards', () => {
  let me: Me | null;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { load: async () => me } }],
    });
  });

  const run = (guard: typeof signedInGuard, url = '/matches') =>
    TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot)) as Promise<boolean | UrlTree>;

  const urlOf = (result: boolean | UrlTree) =>
    result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;

  it('signed out → login page that remembers where you were going', async () => {
    me = null;
    expect(urlOf(await run(signedInGuard, '/matches?view=open'))).toBe('/login?returnUrl=%2Fmatches%3Fview%3Dopen');
  });

  it('signed in → in', async () => {
    me = user('Pending');
    expect(await run(signedInGuard)).toBe(true);
  });

  it('pending → request access page', async () => {
    me = user('Pending');
    expect(urlOf(await run(memberGuard))).toBe('/access');
  });

  it('approved and suspended are members (suspended can still read)', async () => {
    me = user('Approved');
    expect(await run(memberGuard)).toBe(true);
    me = user('Suspended');
    expect(await run(memberGuard)).toBe(true);
  });

  it('admin pages only for admins', async () => {
    me = user('Approved');
    expect(urlOf(await run(adminGuard))).toBe('/');
    me = user('Approved', true);
    expect(await run(adminGuard)).toBe(true);
  });

  it('login page is skipped when already signed in', async () => {
    me = user('Approved');
    expect(urlOf(await run(signedOutGuard))).toBe('/');
    me = null;
    expect(await run(signedOutGuard)).toBe(true);
  });
});
