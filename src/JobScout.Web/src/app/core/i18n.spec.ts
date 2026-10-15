import { HttpErrorResponse } from '@angular/common/http';
import { detectLang, lang, setLang, t } from './i18n';
import { aiErrorMessage, problemMessage, statusLabel, VIEWS } from './match-format';

describe('i18n', () => {
  afterEach(() => setLang('cs'));                        // the other specs expect Czech (test-setup.ts)

  it('picks the language like most sites: own choice first, then the browser, Czech and Slovak → Czech', () => {
    expect(detectLang('en', ['cs-CZ'])).toBe('en');
    expect(detectLang(null, ['cs-CZ', 'en'])).toBe('cs');
    expect(detectLang(null, ['sk'])).toBe('cs');
    expect(detectLang(null, ['de-DE', 'cs'])).toBe('en');      // the first preference decides
    expect(detectLang(null, [])).toBe('en');
    expect(detectLang('fr', ['cs'])).toBe('cs');               // nonsense in storage is ignored
  });

  it('switches every text at once and remembers the choice', () => {
    setLang('en');

    expect(lang()).toBe('en');
    expect(t().matches.title).toBe('Jobs');
    expect(statusLabel('Applied')).toBe('Applied');
    expect(VIEWS[0].label).toBe('New');
    expect(localStorage.getItem('jobscout.lang')).toBe('en');

    setLang('cs');
    expect(statusLabel('Applied')).toBe('Reakce odeslána');
  });

  it('in English, Czech API messages are replaced by our own text', () => {
    const gone = new HttpErrorResponse({ status: 410, error: { detail: 'Inzerát již na portálu není k dispozici.' } });
    setLang('en');

    expect(aiErrorMessage(gone)).toBe('The job posting is no longer available on the job portal.');
    expect(problemMessage(gone, 'Fallback.')).toBe('Fallback.');
  });
});
