import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../core/auth.service';
import { Logo } from '../shared/logo';
import { LangSwitch } from '../shared/lang-switch';
import { t } from '../core/i18n';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, Logo, LangSwitch],
  template: `
    <mat-toolbar class="toolbar">
      <a routerLink="/" class="brand"><app-logo [size]="28" /><span>Job Scout</span></a>
      @if (auth.isMember()) {
        <nav class="nav">
          <a routerLink="/matches" routerLinkActive="active">{{ t().shell.matches }}</a>
          <a routerLink="/profile" routerLinkActive="active">{{ t().shell.profile }}</a>
          @if (auth.me()?.isAdmin) { <a routerLink="/admin" routerLinkActive="active">{{ t().shell.admin }}</a> }
        </nav>
      }
      <span class="spacer"></span>
      <app-lang-switch class="lang" />
      @if (auth.me(); as me) {
        <button mat-button class="user" [matMenuTriggerFor]="userMenu">
          <span class="avatar" aria-hidden="true">{{ initials(me.displayName) }}</span>
          <span class="name">{{ me.displayName }}</span>
        </button>
        <mat-menu #userMenu="matMenu">
          <div class="menu-head">{{ me.email || me.identity }}</div>
          <button mat-menu-item (click)="signOut()"><mat-icon>logout</mat-icon> {{ t().shell.signOut }}</button>
        </mat-menu>
      }
    </mat-toolbar>

    @if (auth.me()?.access === 'Suspended') {
      <div class="suspended" role="alert">
        <strong>{{ t().shell.suspendedTitle }}</strong> {{ auth.me()?.suspendedReason }}
        {{ t().shell.suspendedText }}
        <a routerLink="/access">{{ t().shell.askRestore }}</a>
      </div>
    }

    <main class="content"><router-outlet /></main>
  `,
  styles: `
    .toolbar { position: sticky; top: 0; z-index: 10; gap: 4px; border-bottom: 1px solid var(--js-border); padding: 0 16px; }
    .brand { display: inline-flex; align-items: center; gap: 10px; margin-right: 20px; color: inherit; text-decoration: none;
             font-weight: 650; font-size: 1.05rem; letter-spacing: -0.01em; }
    .nav { display: flex; align-self: stretch; gap: 4px; }
    .nav a { display: inline-flex; align-items: center; padding: 0 12px; color: var(--js-muted); text-decoration: none;
             font-size: 0.95rem; font-weight: 500; border-bottom: 2px solid transparent; margin-bottom: -1px; }
    .nav a:hover { color: var(--js-text); }
    .nav a.active { color: var(--js-text); border-bottom-color: var(--js-primary); }
    .spacer { flex: 1; }
    .lang { margin-right: 8px; }
    .user { color: var(--js-text); }
    .avatar { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 50%;
              background: #e4eefa; color: var(--js-primary); font-size: 0.75rem; font-weight: 700; margin-right: 8px; }
    .menu-head { padding: 8px 16px; font-size: 0.8rem; color: var(--js-muted); border-bottom: 1px solid var(--js-border); }
    .suspended { padding: 12px 16px; background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .content { max-width: 1120px; margin: 0 auto; padding: 24px 16px 48px; }
    @media (max-width: 640px) {
      .brand span, .name { display: none; }
      .brand { margin-right: 8px; }
      .nav a { padding: 0 8px; }
      .avatar { margin-right: 0; }
    }
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly t = t;
  private readonly router = inject(Router);

  constructor() {
    // Coming back to the tab = the admin may have approved or suspended the account meanwhile – ask again
    const refresh = () => {
      if (document.visibilityState === 'visible' && this.auth.me()) void this.auth.load(true).catch(() => undefined);
    };
    document.addEventListener('visibilitychange', refresh);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('visibilitychange', refresh));
  }

  protected initials(name: string): string {
    const parts = name.trim().split(/\s+/).filter(Boolean);
    return ((parts[0]?.[0] ?? '?') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
  }

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/login']);
  }
}
