import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TextFieldModule } from '@angular/cdk/text-field';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { AuthService } from '../../core/auth.service';
import { FeedbackService, MAX_FEEDBACK } from '../../core/feedback.service';
import { FeedbackKind } from '../../core/admin.models';
import { t } from '../../core/i18n';

/** "Zpětná vazba" (13 Oct): what the users miss, what is broken, what they like – straight to the author's inbox. */
@Component({
  selector: 'app-feedback-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, TextFieldModule, MatCardModule, MatButtonModule, MatIconModule, MatChipsModule, MatFormFieldModule,
    MatInputModule, MatCheckboxModule,
  ],
  template: `
    <header class="page-head">
      <h1>{{ t().feedback.title }}</h1>
      <p>{{ t().feedback.intro }}</p>
    </header>

    @if (sent()) {
      <mat-card appearance="outlined" class="card thanks" role="status">
        <mat-card-content>
          <mat-icon class="big">volunteer_activism</mat-icon>
          <h2>{{ t().feedback.sentTitle }}</h2>
          <p class="muted">{{ t().feedback.sentText }}</p>
        </mat-card-content>
        <mat-card-actions>
          <button mat-stroked-button (click)="again()">{{ t().feedback.another }}</button>
        </mat-card-actions>
      </mat-card>
    } @else {
      <mat-card appearance="outlined" class="card">
        <form (ngSubmit)="send()">
          <mat-card-content>
            <h2 class="label" id="kind-label">{{ t().feedback.kindLabel }}</h2>
            <mat-chip-listbox aria-labelledby="kind-label" [ngModel]="kind()" (ngModelChange)="pickKind($event)"
                              name="kind">
              @for (k of kinds; track k.value) {
                <mat-chip-option [value]="k.value"><mat-icon matChipAvatar>{{ k.icon }}</mat-icon>{{ t().feedback.kinds[k.value] }}</mat-chip-option>
              }
            </mat-chip-listbox>

            <h2 class="label" id="rating-label">{{ t().feedback.ratingLabel }}</h2>
            <div class="stars" role="radiogroup" aria-labelledby="rating-label">
              @for (n of [1, 2, 3, 4, 5]; track n) {
                <button type="button" mat-icon-button role="radio" [attr.aria-checked]="rating() === n"
                        [attr.aria-label]="t().feedback.star(n)" (click)="rating.set(rating() === n ? null : n)"
                        [class.on]="(rating() ?? 0) >= n">
                  <mat-icon>{{ (rating() ?? 0) >= n ? 'star' : 'star_outline' }}</mat-icon>
                </button>
              }
              @if (rating()) {
                <button type="button" mat-button class="clear" (click)="rating.set(null)">{{ t().feedback.clearRating }}</button>
              }
            </div>

            <mat-form-field appearance="outline" class="full">
              <mat-label>{{ t().feedback.message }}</mat-label>
              <textarea matInput name="message" [maxlength]="max" cdkTextareaAutosize cdkAutosizeMinRows="6" cdkAutosizeMaxRows="20"
                        [placeholder]="t().feedback.placeholders[kind() ?? 'Other']"
                        [ngModel]="message()" (ngModelChange)="message.set($event)"></textarea>
              <mat-hint align="end">{{ message().length }} / {{ max }}</mat-hint>
            </mat-form-field>

            @if (email(); as address) {
              <mat-checkbox name="reply" [ngModel]="replyWanted()" (ngModelChange)="replyWanted.set($event)">
                {{ t().feedback.reply(address) }}
              </mat-checkbox>
            } @else {
              <p class="muted small">{{ t().feedback.replyDemo }}</p>
            }
            <p class="muted small">{{ t().feedback.privacy }}</p>
            @if (error(); as e) { <p class="error" role="alert">{{ e }}</p> }
          </mat-card-content>
          <mat-card-actions>
            <button mat-flat-button type="submit" [disabled]="!canSend()">
              <mat-icon>send</mat-icon> {{ t().feedback.send }}
            </button>
          </mat-card-actions>
        </form>
      </mat-card>
    }
  `,
  styles: `
    .card { max-width: 720px; }
    .label { font-size: 0.95rem; font-weight: 600; margin: 8px 0 10px; }
    .label:not(:first-child) { margin-top: 24px; }
    .stars { display: flex; align-items: center; gap: 2px; margin-bottom: 16px; }
    .stars .mat-icon { color: var(--js-muted); }
    .stars .on .mat-icon { color: #e3a008; }
    .clear { margin-left: 8px; }
    .full { width: 100%; }
    .small { font-size: 0.85em; }
    .error { color: var(--mat-sys-error); }
    .thanks mat-card-content { text-align: center; padding: 24px 16px 8px; }
    .thanks h2 { margin: 8px 0 4px; font-size: 1.2rem; }
    .thanks mat-card-actions { justify-content: center; padding-bottom: 16px; }
    .big { font-size: 40px; width: 40px; height: 40px; color: var(--js-primary); }
  `,
})
export class FeedbackPage {
  private readonly feedback = inject(FeedbackService);
  private readonly auth = inject(AuthService);

  protected readonly t = t;
  protected readonly max = MAX_FEEDBACK;
  protected readonly kinds: { value: FeedbackKind; icon: string }[] = [
    { value: 'Idea', icon: 'lightbulb' },
    { value: 'Problem', icon: 'bug_report' },
    { value: 'Praise', icon: 'favorite' },
    { value: 'Other', icon: 'chat' },
  ];

  protected readonly kind = signal<FeedbackKind | null>(null);
  protected readonly rating = signal<number | null>(null);
  protected readonly message = signal('');
  protected readonly replyWanted = signal(false);
  protected readonly sending = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The demo account and test personas have no mailbox – nobody to answer to. */
  protected readonly email = computed(() => (this.auth.me()?.isDemo ? null : this.auth.me()?.email || null));
  protected readonly canSend = computed(() => !!this.kind() && this.message().trim().length > 0 && !this.sending());

  protected pickKind(kind: FeedbackKind | null | undefined): void {
    this.kind.set(kind ?? null);
  }

  protected async send(): Promise<void> {
    const kind = this.kind();
    if (!kind || !this.canSend()) return;
    this.sending.set(true);
    this.error.set(null);
    try {
      await this.feedback.send({
        kind, rating: this.rating(), message: this.message().trim(), replyWanted: !!this.email() && this.replyWanted(),
      });
      this.sent.set(true);
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 429 ? t().feedback.tooMany : t().feedback.failed);
    } finally {
      this.sending.set(false);
    }
  }

  protected again(): void {
    this.kind.set(null);
    this.rating.set(null);
    this.message.set('');
    this.replyWanted.set(false);
    this.sent.set(false);
  }
}
