import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeCs from '@angular/common/locales/cs';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { cs } from './i18n/cs';
import { Dict, en } from './i18n/en';

// Date and number formats for the pipes ("12. 10. 2026"). English data is built into Angular, Czech is registered here –
// next to the language itself, so the app and the tests get it the same way.
registerLocaleData(localeCs);

export type Lang = 'cs' | 'en';
export const LANGS: Lang[] = ['cs', 'en'];
const STORAGE_KEY = 'jobscout.lang';

/**
 * The usual rule: the user's own choice (remembered in this browser) wins; otherwise the browser's preferred
 * languages – Czech or Slovak → Czech, anything else → English (Slovaks read Czech, the rest of the world English).
 */
export function detectLang(stored: string | null, browser: readonly string[]): Lang {
  if (stored === 'cs' || stored === 'en') return stored;
  const first = browser.map(l => l.toLowerCase().slice(0, 2)).find(l => l.length === 2);
  return first === 'cs' || first === 'sk' ? 'cs' : 'en';
}

function readStored(): string | null {
  try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }   // blocked storage = no memory, no crash
}

/** The current language. Plain signals, not a service: pure helpers (match-format) read them too. */
export const lang = signal<Lang>(detectLang(readStored(), typeof navigator === 'undefined' ? [] : navigator.languages ?? []));

/** The texts of the current language – templates read t().section.key, so a switch redraws everything at once. */
export const t = computed<Dict>(() => (lang() === 'cs' ? cs : en));

export function setLang(value: Lang): void {
  lang.set(value);
  try { localStorage.setItem(STORAGE_KEY, value); } catch { /* not remembered – still switched */ }
}

/**
 * Route titles are keys of t().titles ("matches") – this strategy turns them into "Nabídky – Job Scout" / "Jobs – Job Scout"
 * and sets <html lang>, also again when the language changes on the same page.
 */
@Injectable({ providedIn: 'root' })
export class I18nTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly key = signal<string | undefined>(undefined);

  constructor() {
    super();
    effect(() => {
      const titles = t().titles as Record<string, string>;
      const key = this.key();
      this.title.setTitle(key && titles[key] ? `${titles[key]} – Job Scout` : 'Job Scout');
      document.documentElement.lang = lang();
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.key.set(this.buildTitle(snapshot));
  }
}
