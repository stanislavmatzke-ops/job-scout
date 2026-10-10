import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule],
  template: `
    <mat-toolbar class="toolbar">
      <a routerLink="/" class="brand">Job Scout</a>
      @if (auth.isMember()) {
        <a mat-button routerLink="/matches" routerLinkActive="active">Nabídky</a>
        <a mat-button routerLink="/profile" routerLinkActive="active">Profil</a>
        @if (auth.me()?.isAdmin) { <a mat-button routerLink="/admin" routerLinkActive="active">Správa</a> }
      }
      <span class="spacer"></span>
      @if (auth.me(); as me) {
        <button mat-button [matMenuTriggerFor]="userMenu">
          <mat-icon>account_circle</mat-icon> {{ me.displayName }}
        </button>
        <mat-menu #userMenu="matMenu">
          <button mat-menu-item (click)="signOut()"><mat-icon>logout</mat-icon> Odhlásit se</button>
        </mat-menu>
      }
    </mat-toolbar>

    @if (auth.me()?.access === 'Suspended') {
      <div class="suspended" role="alert">
        <strong>Účet je pozastaven.</strong> {{ auth.me()?.suspendedReason }}
        Své nabídky si můžete dál prohlížet, vyhledávání nových nabídek a funkce AI jsou však vypnuté.
        <a routerLink="/access">Požádat o obnovení přístupu</a>
      </div>
    }

    <main class="content"><router-outlet /></main>
  `,
  styles: `
    .toolbar { position: sticky; top: 0; z-index: 10; gap: 8px; }
    .brand { font-weight: 600; margin-right: 16px; color: inherit; text-decoration: none; }
    .spacer { flex: 1; }
    .active { text-decoration: underline; }
    .suspended { padding: 12px 16px; background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .content { max-width: 1100px; margin: 0 auto; padding: 24px 16px; }
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  constructor() {
    // Coming back to the tab = the admin may have approved or suspended the account meanwhile – ask again
    const refresh = () => {
      if (document.visibilityState === 'visible' && this.auth.me()) void this.auth.load(true).catch(() => undefined);
    };
    document.addEventListener('visibilitychange', refresh);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('visibilitychange', refresh));
  }

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/login']);
  }
}
