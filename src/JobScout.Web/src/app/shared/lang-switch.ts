import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LANGS, Lang, lang, setLang, t } from '../core/i18n';

/** CS | EN – a two-option segmented switch; the active one is marked for screen readers too. */
@Component({
  selector: 'app-lang-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="switch" role="group" [attr.aria-label]="t().lang.label">
      @for (l of langs; track l) {
        <button type="button" [class.on]="lang() === l" [attr.aria-pressed]="lang() === l"
                [attr.lang]="l" [attr.title]="t().lang[l]" (click)="choose(l)">{{ l.toUpperCase() }}</button>
      }
    </div>
  `,
  styles: `
    .switch { display: inline-flex; border: 1px solid var(--js-border); border-radius: 8px; overflow: hidden; background: var(--js-surface); }
    button { border: 0; background: transparent; padding: 4px 10px; font: inherit; font-size: 0.78rem; font-weight: 600;
             color: var(--js-muted); cursor: pointer; letter-spacing: 0.02em; }
    button + button { border-left: 1px solid var(--js-border); }
    button.on { background: #e4eefa; color: var(--js-primary); }
  `,
})
export class LangSwitch {
  protected readonly langs = LANGS;
  protected readonly lang = lang;
  protected readonly t = t;

  protected choose(value: Lang): void {
    setLang(value);
  }
}
