import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { vi } from 'vitest';
import { apiInterceptor } from './api.interceptor';
import { AuthService } from './auth.service';

/** ADR-0011 on the client side: the CSRF header on every state-changing call, 401 = back to the login page. */
describe('apiInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let router: Router;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([apiInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('adds X-CSRF to state-changing API calls', () => {
    client.post('/api/matches/1/status', { status: 'Applied' }).subscribe();
    client.put('/api/profile/notes', { notes: 'x' }).subscribe();

    expect(http.expectOne('/api/matches/1/status').request.headers.get('X-CSRF')).toBe('1');
    expect(http.expectOne('/api/profile/notes').request.headers.get('X-CSRF')).toBe('1');
  });

  it('leaves reads alone', () => {
    client.get('/api/matches').subscribe();

    expect(http.expectOne('/api/matches').request.headers.has('X-CSRF')).toBe(false);
  });

  it('never sends the header to other sites', () => {
    client.post('https://example.com/api/x', {}).subscribe();

    expect(http.expectOne('https://example.com/api/x').request.headers.has('X-CSRF')).toBe(false);
  });

  it('401 from the API means the session expired – sign out locally and go to the login page', async () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const markSignedOut = vi.spyOn(auth, 'markSignedOut');

    const call = firstValueFrom(client.get('/api/matches'));
    http.expectOne('/api/matches').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toBeTruthy();
    expect(markSignedOut).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login'], expect.anything());
  });

  it('401 from /api/me is not redirected – that is just "not signed in"', async () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const call = firstValueFrom(client.get('/api/me'));
    http.expectOne('/api/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toBeTruthy();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('403 means the access changed meanwhile – "who am I" is reloaded (suspension banner appears)', async () => {
    const load = vi.spyOn(auth, 'load').mockResolvedValue(null);

    const call = firstValueFrom(client.post('/api/matches/1/evaluate', null));
    http.expectOne('/api/matches/1/evaluate').flush(null, { status: 403, statusText: 'Forbidden' });

    await expect(call).rejects.toBeTruthy();
    expect(load).toHaveBeenCalledWith(true);
  });
});
