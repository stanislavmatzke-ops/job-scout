import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { t } from '../../core/i18n';

/** Any unknown address inside the app – an old bookmark, a mistyped link, a job that is no longer ours. */
@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule],
  template: `
    <div class="box">
      <mat-icon class="big">explore_off</mat-icon>
      <h1>{{ t().notFound.title }}</h1>
      <p>{{ t().notFound.text }}</p>
      <a mat-flat-button routerLink="/">{{ t().notFound.home }}</a>
    </div>
  `,
  styles: `
    .box { text-align: center; padding: 48px 16px; }
    .big { font-size: 64px; width: 64px; height: 64px; opacity: 0.5; }
    p { color: var(--js-muted); }
  `,
})
export class NotFoundPage {
  protected readonly t = t;
}
