import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SiteService } from '../core/site.service';
import { t } from '../core/i18n';

/** On every page: what the app is, the privacy page (GDPR) and the "about" link from the API's configuration. */
@Component({
  selector: 'app-site-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <footer class="footer">
      <span>{{ t().footer.about }}</span>
      <nav class="links" [attr.aria-label]="t().footer.links">
        <a routerLink="/privacy">{{ t().footer.privacy }}</a>
        @if (site.site()?.aboutUrl; as url) {
          <a [href]="url" target="_blank" rel="noopener">{{ t().footer.project }}</a>
        }
      </nav>
    </footer>
  `,
  styles: `
    .footer { display: flex; flex-wrap: wrap; justify-content: center; gap: 4px 16px; padding: 20px 16px 28px;
              border-top: 1px solid var(--js-border); color: var(--js-muted); font-size: 0.85rem; text-align: center; }
    .links { display: flex; gap: 16px; }
    .links a { color: inherit; }
    .links a:hover { color: var(--js-text); }
  `,
})
export class SiteFooter {
  protected readonly site = inject(SiteService);
  protected readonly t = t;

  constructor() {
    void this.site.load();
  }
}
