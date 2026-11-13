import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { formatDate } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Logo } from '../../shared/logo';
import { LangSwitch } from '../../shared/lang-switch';
import { SiteFooter } from '../../shared/site-footer';
import { SiteService } from '../../core/site.service';
import { lang, t } from '../../core/i18n';

/** When the text below last changed – shown on the page (GDPR: people should see what is current). */
export const PRIVACY_UPDATED = '2026-11-13';

/**
 * /privacy – what we store, why, who else gets it and for how long (GDPR). Public: readable before signing in.
 * The controller and the contact come from the API's configuration (Operator:*), the rest is in i18n (cs, en).
 */
@Component({
  selector: 'app-privacy-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Logo, LangSwitch, SiteFooter],
  template: `
    <header class="top">
      <a routerLink="/" class="brand"><app-logo [size]="28" /><span>Job Scout</span></a>
      <span class="spacer"></span>
      <app-lang-switch />
    </header>

    <main class="content">
      <h1>{{ t().privacy.title }}</h1>
      <p class="updated">{{ t().privacy.updated(updated()) }}</p>
      <p class="intro">{{ t().privacy.intro }}</p>

      @if (site.failed() && !site.site()) {
        <p class="error" role="alert">{{ t().privacy.loadFailed }}</p>
      }

      @for (section of sections(); track $index) {
        <section>
          <h2>{{ section.title }}</h2>
          @for (paragraph of section.text ?? []; track $index) { <p>{{ paragraph }}</p> }
          @if (section.items?.length) {
            <ul>@for (item of section.items; track $index) { <li>{{ item }}</li> }</ul>
          }
        </section>
      }
    </main>

    <app-site-footer />
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 100vh; }
    .top { display: flex; align-items: center; gap: 8px; padding: 10px 16px; border-bottom: 1px solid var(--js-border); }
    .brand { display: inline-flex; align-items: center; gap: 10px; color: inherit; text-decoration: none; font-weight: 650; }
    .spacer { flex: 1; }
    .content { flex: 1; width: 100%; max-width: 760px; margin: 0 auto; padding: 24px 16px 40px; box-sizing: border-box; line-height: 1.6; }
    h1 { margin: 0 0 4px; font-size: 1.7rem; font-weight: 600; }
    .updated { margin: 0 0 16px; color: var(--js-muted); font-size: 0.9em; }
    .intro { font-size: 1.05rem; }
    h2 { margin: 28px 0 6px; font-size: 1.15rem; font-weight: 600; }
    ul { padding-left: 20px; margin: 6px 0; }
    li { margin-bottom: 6px; }
  `,
})
export class PrivacyPage {
  protected readonly site = inject(SiteService);
  protected readonly t = t;
  protected readonly updated = computed(() => formatDate(PRIVACY_UPDATED, t().fmt.date, lang()));

  /** The operator is filled in as soon as /api/site answers; until then "…". */
  protected readonly sections = computed(() => {
    const s = this.site.site();
    return t().privacy.sections({ name: s?.operatorName || '…', email: s?.operatorEmail || '…' });
  });

  constructor() {
    void this.site.load();
  }
}
