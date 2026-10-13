import { HttpErrorResponse } from '@angular/common/http';
import { setLang } from './i18n';
import { AiLimits, blockedMessage, limitMessage, usageHint } from './limits.service';
import { aiErrorMessage } from './match-format';
import { t } from './i18n';

const usage = (used: number, limit: number, resetsAt: string | null = null) => ({ used, limit, left: Math.max(0, limit - used), resetsAt });

function limits(overrides: Partial<AiLimits> = {}): AiLimits {
  return {
    unlimited: false, profile: usage(1, 3), coverLetters: usage(0, 10), evaluations: usage(0, 20),
    letterRewritesPerJob: 2, costUsd: 0.02, costLimitUsd: 0.5, costResetsAt: null, paused: false,
    ...overrides,
  };
}

const tooMany = (body: object) => new HttpErrorResponse({ status: 429, error: body });

/** ADR-0021 in the UI: what is left, why a button is refused – in the user's language. */
describe('AI limits', () => {
  afterEach(() => setLang('cs'));

  it('says how many clicks are left', () => {
    expect(usageHint(limits(), l => l.profile, t().limits.profileLeft)).toBe('Nové vyhodnocení profilu: zbývá 2 z 3 za 24 hodin.');
  });

  it('when nothing is left, says when it frees up again', () => {
    const hint = usageHint(limits({ profile: usage(3, 3, '2026-10-14T10:30:00Z') }), l => l.profile, t().limits.profileLeft);

    expect(hint).toContain('zbývá 0 z 3');
    expect(hint).toContain('Znovu to půjde 14. 10. 2026');
  });

  it('admins see no counts at all', () => {
    expect(usageHint(limits({ unlimited: true }), l => l.profile, t().limits.profileLeft)).toBeNull();
    expect(blockedMessage(limits({ unlimited: true, paused: true }))).toBeNull();
  });

  it('the money limits block every AI button', () => {
    expect(blockedMessage(limits())).toBeNull();
    expect(blockedMessage(limits({ costUsd: 0.5 }))).toContain('Dnešní limit funkcí AI je vyčerpaný');
    expect(blockedMessage(limits({ paused: true }))).toContain('pozastavené');
  });

  it('a 429 from the API is worded by its reason, in the current language', () => {
    const error = tooMany({ reason: 'CoverLetter', limit: 10, retryAt: '2026-10-14T10:30:00Z', detail: 'Česky z API' });

    expect(limitMessage(error)).toContain('Průvodní dopis lze nechat napsat nejvýš 10× za 24 hodin.');
    expect(aiErrorMessage(error)).toContain('Znovu to půjde');

    setLang('en');
    expect(aiErrorMessage(error)).toContain('Cover letters can be written at most 10 times in 24 hours.');
  });

  it('a 429 without a known reason still says something sensible', () => {
    expect(limitMessage(tooMany({}))).toBe(t().errors.ai429);
    expect(limitMessage(tooMany({ reason: 'constructor' }))).toBe(t().errors.ai429);   // only our own keys
    expect(limitMessage(tooMany({ reason: 'LetterRewrites', limit: 2 }))).toContain('nový návrh 2×');
  });
});
