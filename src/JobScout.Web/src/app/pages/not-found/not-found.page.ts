import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/** Any unknown address inside the app – an old bookmark, a mistyped link, a job that is no longer ours. */
@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule],
  template: `
    <div class="box">
      <mat-icon class="big">explore_off</mat-icon>
      <h1>Stránka nenalezena</h1>
      <p>Adresa, kterou jste otevřeli, v aplikaci neexistuje. Možná jde o starý odkaz nebo překlep.</p>
      <a mat-flat-button routerLink="/">Přejít na nabídky</a>
    </div>
  `,
  styles: `
    .box { text-align: center; padding: 48px 16px; }
    .big { font-size: 64px; width: 64px; height: 64px; opacity: 0.5; }
    p { opacity: 0.8; }
  `,
})
export class NotFoundPage {}
