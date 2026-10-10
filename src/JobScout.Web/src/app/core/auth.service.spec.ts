import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { Me } from './models';

const jana: Me = {
  id: '01', provider: 'google', displayName: 'Jana', email: 'jana@example.com', access: 'Approved',
  accessRequestedAt: null, suspendedReason: null, isAdmin: false, identity: 'google:g-1',
};

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('knows nobody before loading', () => {
    expect(auth.me()).toBeUndefined();
  });

  it('loads the signed-in user from /api/me', async () => {
    const loading = auth.load();
    http.expectOne('/api/me').flush(jana);

    expect(await loading).toEqual(jana);
    expect(auth.me()).toEqual(jana);
    expect(auth.isMember()).toBe(true);
  });

  it('treats 401 as signed out, not as an error', async () => {
    const loading = auth.load();
    http.expectOne('/api/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await loading).toBeNull();
    expect(auth.me()).toBeNull();
  });

  it('asks the API only once unless forced', async () => {
    const first = auth.load();
    http.expectOne('/api/me').flush(jana);
    await first;

    await auth.load();                    // from memory – no request
    http.expectNone('/api/me');

    const forced = auth.load(true);
    http.expectOne('/api/me').flush({ ...jana, displayName: 'Jana N.' });
    expect((await forced)?.displayName).toBe('Jana N.');
  });

  it('pending user is not a member yet', async () => {
    const loading = auth.load();
    http.expectOne('/api/me').flush({ ...jana, access: 'Pending' });
    await loading;

    expect(auth.isMember()).toBe(false);
  });

  it('access request is sent and the user reloaded', async () => {
    const requesting = auth.requestAccess('Hledám práci');
    const post = http.expectOne('/api/access/request');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ message: 'Hledám práci' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    await new Promise(resolve => setTimeout(resolve));   // let the service continue to the reload

    http.expectOne('/api/me').flush({ ...jana, access: 'Pending', accessRequestedAt: '2026-10-10T12:00:00Z' });
    expect((await requesting)?.accessRequestedAt).toBe('2026-10-10T12:00:00Z');
  });

  it('sign-out forgets the user', async () => {
    const loading = auth.load();
    http.expectOne('/api/me').flush(jana);
    await loading;

    const signingOut = auth.signOut();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await signingOut;

    expect(auth.me()).toBeNull();
  });
});
