import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../../core/auth.service';
import { AuthProviders } from '../../core/models';

const ERRORS: Record<string, string> = {
  'invitation-invalid': 'Pozvánka je neplatná, již byla použita, nebo vypršela její platnost.',
  'no-identity': 'Přihlášení se nezdařilo – poskytovatel nevrátil údaje o účtu. Zkuste to prosím znovu.',
};

const PROVIDER_NAMES: Record<string, string> = { microsoft: 'Microsoft', google: 'Google' };

@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  template: `
    <mat-card class="card">
      <mat-card-header>
        <mat-card-title>Job Scout</mat-card-title>
        <mat-card-subtitle>AI předvybírá, člověk rozhoduje</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (errorText(); as text) { <p class="error" role="alert">{{ text }}</p> }

        <p>Přihlaste se svým účtem Microsoft nebo Google. Job Scout neukládá žádná hesla.</p>
        @for (provider of providers()?.external ?? []; track provider) {
          <button mat-flat-button class="provider" (click)="signIn(provider)">
            Přihlásit se přes {{ providerName(provider) }}
          </button>
        }
        <p class="hint">Používejte prosím vždy stejný účet – přihlášení přes Microsoft a přes Google jsou vedena jako dva samostatné účty.</p>

        @if (providers()?.devSignIn) {
          <form class="dev" (ngSubmit)="devSignIn()">
            <mat-form-field appearance="outline">
              <mat-label>Testovací persona (pouze vývojové prostředí)</mat-label>
              <input matInput name="persona" [(ngModel)]="persona" placeholder="private, zednik, …" />
            </mat-form-field>
            <button mat-stroked-button type="submit" [disabled]="!persona">Přihlásit se jako persona</button>
          </form>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: flex; justify-content: center; padding: 48px 16px; }
    .card { width: 100%; max-width: 420px; }
    .provider { display: block; width: 100%; margin: 8px 0; }
    .hint { font-size: 0.85em; opacity: 0.7; }
    .error { color: var(--mat-sys-error); }
    .dev { margin-top: 24px; display: flex; flex-direction: column; gap: 4px; }
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
  protected persona = '';

  protected errorText = () => (this.error() ? ERRORS[this.error()!] ?? 'Přihlášení se nezdařilo.' : null);
  protected providerName = (p: string) => PROVIDER_NAMES[p] ?? p;

  async ngOnInit(): Promise<void> {
    this.providers.set(await this.auth.providers());
  }

  protected signIn(provider: string): void {
    this.auth.signInWith(provider, this.returnUrl(), this.invite());
  }

  protected async devSignIn(): Promise<void> {
    await this.auth.devSignIn(this.persona.trim());
    await this.router.navigateByUrl(this.returnUrl());
  }
}
