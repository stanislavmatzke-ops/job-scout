import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../../core/auth.service';

/** ADR-0017: Pending → ask for access; Suspended → see why and ask again; Approved → nothing to do here. */
@Component({
  selector: 'app-access-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  template: `
    @if (auth.me(); as me) {
      <mat-card class="card">
        @switch (me.access) {
          @case ('Approved') {
            <mat-card-content>
              <p>Váš účet je aktivní. <a routerLink="/matches">Přejít na nabídky</a></p>
            </mat-card-content>
          }
          @default {
            <mat-card-header>
              <mat-card-title>{{ me.access === 'Suspended' ? 'Účet je pozastaven' : 'Vítejte, ' + me.displayName }}</mat-card-title>
            </mat-card-header>
            <mat-card-content>
              @if (me.access === 'Suspended') {
                <p class="reason">{{ me.suspendedReason }}</p>
              } @else {
                <p>
                  Přístup do Job Scout je zatím omezený a každý účet schvalujeme jednotlivě.
                  Odešlete prosím žádost o přístup – v krátké zprávě můžete uvést, jakou práci hledáte, nebo proč si aplikaci chcete vyzkoušet.
                </p>
              }

              @if (me.accessRequestedAt) {
                <p class="sent">
                  Vaši žádost jsme přijali {{ me.accessRequestedAt | date: 'd. M. y H:mm' }}.
                  O jejím vyřízení vás budeme informovat e-mailem. Po schválení se přihlásíte jako obvykle.
                </p>
              } @else {
                <form (ngSubmit)="request()">
                  <mat-form-field appearance="outline" class="full">
                    <mat-label>Zpráva (nepovinné)</mat-label>
                    <textarea matInput name="message" rows="3" maxlength="1000" [(ngModel)]="message"
                              placeholder="Např. hledám pozici .NET vývojáře v Brně / jsem z HR a zajímá mě, jak aplikace funguje"></textarea>
                  </mat-form-field>
                  <button mat-flat-button type="submit" [disabled]="sending()">
                    {{ me.access === 'Suspended' ? 'Požádat o obnovení přístupu' : 'Odeslat žádost o přístup' }}
                  </button>
                </form>
              }
            </mat-card-content>
          }
        }
      </mat-card>
    }
  `,
  styles: `
    .card { max-width: 560px; margin: 0 auto; }
    .full { width: 100%; }
    .reason { font-weight: 500; }
    .sent { color: var(--mat-sys-primary); }
  `,
})
export class AccessPage {
  protected readonly auth = inject(AuthService);
  protected readonly sending = signal(false);
  protected message = '';

  protected async request(): Promise<void> {
    this.sending.set(true);
    try {
      await this.auth.requestAccess(this.message.trim() || null);
    } finally {
      this.sending.set(false);
    }
  }
}
