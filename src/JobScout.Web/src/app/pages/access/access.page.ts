import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../../core/auth.service';
import { lang, t } from '../../core/i18n';

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
              <p>{{ t().access.active }} <a routerLink="/matches">{{ t().access.goMatches }}</a></p>
            </mat-card-content>
          }
          @default {
            <mat-card-header>
              <mat-card-title>{{ me.access === 'Suspended' ? t().access.suspendedTitle : me.rejectedAt ? t().access.rejectedTitle : t().access.welcome(me.displayName) }}</mat-card-title>
            </mat-card-header>
            <mat-card-content>
              @if (me.access === 'Suspended') {
                <p class="reason">{{ me.suspendedReason }}</p>
              } @else if (me.rejectedAt) {
                @if (me.rejectedReason) { <p class="reason">{{ me.rejectedReason }}</p> }
                <p>{{ t().access.rejectedAgain }}</p>
              } @else {
                <p>{{ t().access.intro }}</p>
              }

              @if (me.accessRequestedAt) {
                <p class="sent">{{ t().access.received((me.accessRequestedAt | date: t().fmt.dateTime : undefined : lang()) ?? '') }}</p>
              } @else {
                <form (ngSubmit)="request()">
                  <mat-form-field appearance="outline" class="full">
                    <mat-label>{{ t().access.message }}</mat-label>
                    <textarea matInput name="message" rows="3" maxlength="1000" [(ngModel)]="message"
                              [placeholder]="t().access.placeholder"></textarea>
                  </mat-form-field>
                  <button mat-flat-button type="submit" [disabled]="sending()">
                    {{ me.access === 'Suspended' ? t().access.requestRestore : me.rejectedAt ? t().access.sendAgain : t().access.send }}
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
  protected readonly t = t;
  protected readonly lang = lang;
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
