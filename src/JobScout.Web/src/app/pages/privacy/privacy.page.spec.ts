import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PrivacyPage } from './privacy.page';
import { LoginPage } from '../login/login.page';
import { ShellComponent } from '../../layout/shell.component';
import { AuthService } from '../../core/auth.service';
import { SiteService } from '../../core/site.service';

const site = { operatorName: 'Jana Provozovatelka', operatorEmail: 'privacy@example.com', aboutUrl: 'https://example.com/about' };

/**
 * GDPR: the privacy page names the controller and the contact from the API's configuration, and every page –
 * signed in or not – links to it in the footer.
 */
describe('Privacy page and footer', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const settle = () => new Promise(resolve => setTimeout(resolve));
  const privacyLink = (host: HTMLElement) => host.querySelector('app-site-footer a[href="/privacy"]');

  it('names the controller and the contact from the configuration', async () => {
    const fixture = TestBed.createComponent(PrivacyPage);
    fixture.detectChanges();
    http.expectOne('/api/site').flush(site);
    await settle();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Ochrana osobních údajů');
    expect(text).toContain('Správcem osobních údajů je Jana Provozovatelka');
    expect(text).toContain('privacy@example.com');
    expect(text).toContain('Anthropic');
    expect(text).toContain('__Host-jobscout');
    expect(text).toContain('Poslední změna');
  });

  it('the footer links to the privacy page and to the project from the configuration', async () => {
    const fixture = TestBed.createComponent(PrivacyPage);
    fixture.detectChanges();
    http.expectOne('/api/site').flush(site);
    await settle();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(privacyLink(host)).toBeTruthy();
    expect(host.querySelector('app-site-footer a[href="https://example.com/about"]')).toBeTruthy();
  });

  it('the sign-in page has the footer and the notice about the processing', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    http.expectOne('/api/site').flush(site);
    http.expectOne('/api/auth/providers').flush({ external: ['google'], devSignIn: false, demo: null });
    await settle();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(privacyLink(host)).toBeTruthy();
    expect(host.querySelector('.consent a[href="/privacy"]')).toBeTruthy();
    expect(host.textContent).toContain('berete na vědomí');
  });

  it('a signed-in page has the footer too', async () => {
    const auth = TestBed.inject(AuthService);
    const loading = auth.load();
    http.expectOne('/api/me').flush({ id: '1', displayName: 'Jana', email: 'jana@example.com', access: 'Approved' });
    await loading;

    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    http.expectOne('/api/site').flush(site);
    await settle();
    fixture.detectChanges();

    expect(privacyLink(fixture.nativeElement as HTMLElement)).toBeTruthy();
  });

  it('the operator is asked for once, however many footers there are', async () => {
    const service = TestBed.inject(SiteService);
    const first = service.load();
    const second = service.load();
    http.expectOne('/api/site').flush(site);
    await Promise.all([first, second]);

    expect(service.site()?.operatorName).toBe('Jana Provozovatelka');
  });
});
