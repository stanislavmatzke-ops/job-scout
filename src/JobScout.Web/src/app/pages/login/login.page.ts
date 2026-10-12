import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';
import { AuthProviders } from '../../core/models';
import { problemMessage } from '../../core/match-format';
import { Logo } from '../../shared/logo';

const ERRORS: Record<string, string> = {
  'invitation-invalid': 'Pozvánka je neplatná, již byla použita, nebo vypršela její platnost.',
  'no-identity': 'Přihlášení se nezdařilo – poskytovatel nevrátil údaje o účtu. Zkuste to prosím znovu.',
};

/**
 * Official brand marks, as the providers' sign-in guidelines require – downloaded from Google and Microsoft,
 * not redrawn (public/brand/). Missing file = the button simply shows no mark.
 */
const PROVIDERS: Record<string, { name: string; mark: string }> = {
  google: { name: 'Google', mark: 'brand/google.svg' },
  microsoft: { name: 'Microsoft', mark: 'brand/microsoft.svg' },
};

/** The test account offered on the sign-in page (phase 8 turns it into the public demo account). */
export const SAMPLE_PERSONA = 'sample';

@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule, Logo],
  template: `
    <main class="wrap">
      <section class="panel" aria-labelledby="title">
        <app-logo [size]="48" />
        <h1 id="title">Přihlaste se do Job Scout</h1>
        <p class="lead">Každé ráno projdeme nové nabídky práce, AI je ohodnotí podle vašeho životopisu a k nejlepším připraví průvodní dopis.</p>

        @if (errorText(); as text) { <p class="error" role="alert">{{ text }}</p> }

        <div class="providers">
          @for (provider of providers()?.external ?? []; track provider) {
            <button type="button" class="provider" (click)="signIn(provider)">
              <img [src]="mark(provider)" alt="" width="18" height="18" (error)="$any($event.target).hidden = true" />
              <span>Pokračovat přes {{ providerName(provider) }}</span>
            </button>
          }
        </div>

        @if (providers()?.devSignIn) {
          <div class="or"><span>nebo</span></div>

          @if (!sampleOpen()) {
            <button type="button" class="provider sample" (click)="sampleOpen.set(true)">
              <mat-icon>science</mat-icon>
              <span>Vyzkoušet bez registrace</span>
            </button>
          } @else {
            <form class="sample-form" (ngSubmit)="devSignIn()">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Testovací účet</mat-label>
                <input matInput name="persona" [(ngModel)]="persona" autocomplete="off" />
              </mat-form-field>
              <button mat-flat-button type="submit" [disabled]="!persona.trim()">Vstoupit</button>
            </form>
            @if (devError(); as e) { <p class="error" role="alert">{{ e }}</p> }
          }
        }

        <p class="fine">
          Přihlásit se můžete účtem Google nebo Microsoft@if (providers()?.devSignIn) {, případně si aplikaci vyzkoušet jako testovací uživatel}.
        </p>
      </section>
    </main>
  `,
  styles: `
    .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 32px 16px; box-sizing: border-box; }
    .panel {
      width: 100%; max-width: 400px; padding: 36px 32px 24px; box-sizing: border-box;
      background: var(--js-surface); border: 1px solid var(--js-border); border-radius: 14px;
      display: flex; flex-direction: column; align-items: stretch; text-align: center;
    }
    app-logo { align-self: center; margin-bottom: 16px; }
    h1 { margin: 0 0 8px; font-size: 1.4rem; font-weight: 600; }
    .lead { margin: 0 0 24px; color: var(--js-muted); line-height: 1.5; }
    .providers { display: flex; flex-direction: column; gap: 10px; }
    .provider {
      display: flex; align-items: center; justify-content: center; gap: 10px;
      height: 44px; padding: 0 16px; border-radius: 8px; cursor: pointer;
      background: var(--js-surface); border: 1px solid #c9d1db; color: var(--js-text);
      font: inherit; font-weight: 600; font-size: 0.95rem;
    }
    .provider:hover { background: #f7f9fb; border-color: #aeb8c5; }
    .provider img { flex-shrink: 0; }
    .provider.sample .mat-icon { color: var(--js-ai); }
    .or { display: flex; align-items: center; gap: 12px; margin: 18px 0; color: var(--js-muted); font-size: 0.85rem; }
    .or::before, .or::after { content: ''; flex: 1; border-top: 1px solid var(--js-border); }
    .sample-form { display: flex; gap: 8px; align-items: center; text-align: left; }
    .sample-form mat-form-field { flex: 1; }
    .error { margin: 0 0 16px; text-align: left; }
    .fine { margin: 24px 0 0; font-size: 0.8rem; color: var(--js-muted); line-height: 1.45; }
  `,
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** From the URL: ?returnUrl=… (where to go after sign-in), ?error=… (from the API), ?invite=… */
  readonly returnUrl = input<string>('/');
  readonly error = input<string>();
  readonly invite = input<string>();

  protected readonly providers = signal<AuthProviders | null>(null);
  protected readonly sampleOpen = signal(false);
  protected readonly devError = signal<string | null>(null);
  protected persona = SAMPLE_PERSONA;

  protected errorText = () => (this.error() ? ERRORS[this.error()!] ?? 'Přihlášení se nezdařilo.' : null);
  protected providerName = (p: string) => PROVIDERS[p]?.name ?? p;
  protected mark = (p: string) => PROVIDERS[p]?.mark ?? '';

  async ngOnInit(): Promise<void> {
    this.providers.set(await this.auth.providers());
  }

  protected signIn(provider: string): void {
    this.auth.signInWith(provider, this.returnUrl(), this.invite());
  }

  /** "dev:boss" and "Boss" mean the persona "boss" – the identity shown elsewhere has the prefix, people copy it. */
  protected async devSignIn(): Promise<void> {
    const persona = this.persona.trim().toLowerCase().replace(/^dev:/, '');
    this.devError.set(null);
    try {
      await this.auth.devSignIn(persona);
    } catch (e) {
      this.devError.set(problemMessage(e, 'Přihlášení se nezdařilo.'));
      return;
    }
    await this.router.navigateByUrl(this.returnUrl());
  }
}
