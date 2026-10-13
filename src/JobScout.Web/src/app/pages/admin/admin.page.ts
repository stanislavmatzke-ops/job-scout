import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Clipboard } from '@angular/cdk/clipboard';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { AdminService, MAX_INVITATION_NOTE, formatUsd, invitationLinks } from '../../core/admin.service';
import { AdminUser, CreatedInvitation, FeedbackRow, Invitation, InvitationLink } from '../../core/admin.models';
import { AccessStatus } from '../../core/models';
import { problemMessage } from '../../core/match-format';
import { ConfirmService } from '../../shared/confirm-dialog';
import { SuspendDialog, SuspendData } from './suspend-dialog';
import { lang, t } from '../../core/i18n';

/** 6e – the admin panel (ADR-0017): requests to approve, users with their AI costs, invitations. */
@Component({
  selector: 'app-admin-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatProgressBarModule,
  ],
  template: `
    <header class="page-head">
      <h1>{{ t().admin.title }}</h1>
      <p>{{ t().admin.intro }}</p>
    </header>
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
    @if (error(); as e) { <p class="error" role="alert">{{ e }}</p> }

    <!-- ───── requests ───── -->
    <section>
      <h2>{{ t().admin.requests }} @if (requests().length) { <span class="count">{{ requests().length }}</span> }</h2>
      @for (u of requests(); track u.id) {
        <mat-card appearance="outlined" class="request">
          <mat-card-content>
            <div class="who"><strong>{{ u.displayName }}</strong> <span class="muted">{{ u.email }} · {{ u.provider }}</span></div>
            <div class="muted">{{ t().admin.requested((u.accessRequestedAt | date: t().fmt.dateTime : undefined : lang()) ?? '') }}</div>
            @if (u.accessRequestMessage) { <blockquote>{{ u.accessRequestMessage }}</blockquote> }
            @else { <p class="muted">{{ t().admin.noMessage }}</p> }
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-button [disabled]="busy()" (click)="reject(u)">{{ t().admin.reject }}</button>
            <button mat-flat-button [disabled]="busy()" (click)="approve(u)">{{ t().admin.approve }}</button>
          </mat-card-actions>
        </mat-card>
      } @empty {
        @if (!loading()) { <p class="muted">{{ t().admin.nobodyWaits }}</p> }
      }
    </section>

    <!-- ───── users ───── -->
    <section>
      <h2>{{ t().admin.users }}</h2>
      @if (users().length) {
        <p class="muted totals">
          {{ t().admin.totals }} {{ t().admin.days7 }} <strong>{{ usd(total7()) }}</strong> · {{ t().admin.days30 }} <strong>{{ usd(total30()) }}</strong>
        </p>
      }
      <div class="users">
        @for (u of others(); track u.id) {
          <div class="user" [class.suspended]="u.access === 'Suspended'">
            <div class="main">
              <div class="who">
                <strong>{{ u.displayName }}</strong>
                <span class="chip" [attr.data-access]="u.access">{{ accessLabel(u.access) }}</span>
                @if (isMe(u)) { <span class="chip">{{ t().admin.you }}</span> }
              </div>
              <div class="muted">{{ u.email }} · {{ u.provider }}</div>
              <div class="muted">
                @if (u.lastSignInAt) { {{ t().admin.lastSignIn((u.lastSignInAt | date: t().fmt.dateTime : undefined : lang()) ?? '') }} } @else { {{ t().admin.neverSignedIn }} }
                · {{ u.hasProfile ? t().admin.hasProfile : t().admin.noProfile }}
              </div>
              @if (u.suspendedReason) { <div class="reason">{{ t().admin.reason(u.suspendedReason) }}</div> }
              @if (u.access === 'Pending' && u.rejectedAt) {
                <div class="reason">{{ t().admin.rejected((u.rejectedAt | date: t().fmt.date : undefined : lang()) ?? '') }}@if (u.rejectedReason) {<span> – {{ u.rejectedReason }}</span>}</div>
              }
            </div>
            <div class="cost" [title]="t().admin.costTitle">
              <span>7 d: {{ usd(u.costLast7DaysUsd) }}</span>
              <span>30 d: {{ usd(u.costLast30DaysUsd) }}</span>
              @if (u.clickCostLast24hUsd) { <span class="clicks">{{ t().admin.clicks24 }}: {{ usd(u.clickCostLast24hUsd) }}</span> }
            </div>
            <div class="actions">
              @switch (u.access) {
                @case ('Approved') {
                  @if (!isMe(u)) { <button mat-stroked-button [disabled]="busy()" (click)="suspend(u)">{{ t().admin.suspend }}</button> }
                }
                @case ('Suspended') { <button mat-stroked-button [disabled]="busy()" (click)="approve(u)">{{ t().admin.restore }}</button> }
                @case ('Pending') { <button mat-stroked-button [disabled]="busy()" (click)="approve(u)">{{ t().admin.approve }}</button> }
              }
            </div>
          </div>
        }
      </div>
    </section>

    <!-- ───── feedback (13 Oct) ───── -->
    <section>
      <h2>{{ t().admin.feedback }} @if (feedback().length) { <span class="count">{{ feedback().length }}</span> }</h2>
      @if (feedbackError(); as e) { <p class="error">{{ e }}</p> }
      <div class="feedback">
        @for (f of feedback(); track f.id) {
          <div class="message">
            <div class="who">
              <span class="chip" [attr.data-kind]="f.kind">{{ t().feedback.kinds[f.kind] }}</span>
              <strong>{{ f.userName }}</strong>
              @if (f.rating) { <span class="stars" [attr.aria-label]="t().feedback.star(f.rating)">{{ stars(f.rating) }}</span> }
              <span class="muted">{{ f.createdAt | date: t().fmt.dateTime : undefined : lang() }}</span>
              @if (f.replyWanted && f.email) {
                <a mat-button class="reply" [href]="mailto(f)"><mat-icon>reply</mat-icon> {{ t().admin.reply }}</a>
              }
            </div>
            <p class="text">{{ f.message }}</p>
          </div>
        } @empty {
          @if (!loading()) { <p class="muted">{{ t().admin.noFeedback }}</p> }
        }
      </div>
    </section>

    <!-- ───── invitations ───── -->
    <section>
      <h2>{{ t().admin.invitations }}</h2>
      <p class="muted">{{ t().admin.invitationsHelp }}</p>
      <form class="invite" (ngSubmit)="createInvitation()">
        <mat-form-field appearance="outline" class="note">
          <mat-label>{{ t().admin.inviteFor }}</mat-label>
          <input matInput name="note" [maxlength]="maxNote" [ngModel]="note()" (ngModelChange)="note.set($event)" />
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy() || !note().trim()">{{ t().admin.createInvite }}</button>
      </form>

      @if (created(); as c) {
        <mat-card appearance="outlined" class="created">
          <mat-card-content>
            <p><mat-icon inline>info</mat-icon>
              <strong>{{ t().admin.copyNow }}</strong>{{ t().admin.copyNowRest }}
              {{ t().admin.validUntil((c.invitation.expiresAt | date: t().fmt.date : undefined : lang()) ?? '') }}</p>
            @for (link of c.links; track link.provider) {
              <div class="link">
                <span class="provider">{{ link.label }}</span>
                <code>{{ link.url }}</code>
                <button mat-icon-button (click)="copy(link)" [attr.aria-label]="t().admin.copyLink(link.label)">
                  <mat-icon>content_copy</mat-icon>
                </button>
              </div>
            } @empty {
              <p>{{ t().admin.noProviders }} <code>{{ c.invitation.code }}</code></p>
            }
          </mat-card-content>
        </mat-card>
      }

      <div class="invitations">
        @for (i of invitations(); track i.id) {
          <div class="invitation">
            <strong>{{ i.note || t().admin.noNote }}</strong>
            <span class="muted">{{ t().admin.created((i.createdAt | date: t().fmt.date : undefined : lang()) ?? '') }}</span>
            @if (i.usedAt) {
              <span class="chip" data-access="Approved">{{ t().admin.usedBy(i.usedBy ?? '', (i.usedAt | date: t().fmt.date : undefined : lang()) ?? '') }}</span>
            } @else if (i.isUsable) {
              <span class="chip">{{ t().admin.validTill((i.expiresAt | date: t().fmt.date : undefined : lang()) ?? '') }}</span>
            } @else {
              <span class="chip" data-access="Suspended">{{ t().admin.expired }}</span>
            }
          </div>
        } @empty {
          @if (!loading()) { <p class="muted">{{ t().admin.noInvitations }}</p> }
        }
      </div>
    </section>

    <!-- ───── demo account (ADR-0020) ───── -->
    <section>
      <h2>{{ t().admin.demo }}</h2>
      <p class="muted">{{ t().admin.demoHelp }}</p>
      <button mat-stroked-button [disabled]="busy()" (click)="prepareDemo()">
        <mat-icon>science</mat-icon> {{ t().admin.demoPrepare }}
      </button>
    </section>
  `,
  styles: `
    section { margin-bottom: 32px; }
    h2 { display: flex; align-items: center; gap: 8px; font-size: 1.15rem; font-weight: 600; }
    .count { font-size: 14px; padding: 0 8px; border-radius: 12px; background: var(--mat-sys-primary); color: var(--mat-sys-on-primary); }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: 14px; }
    .error { color: var(--mat-sys-error); }
    .request { margin-bottom: 12px; }
    blockquote { margin: 8px 0 0; padding: 8px 12px; border-left: 3px solid var(--mat-sys-primary); white-space: pre-line; }
    .who { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .users { display: flex; flex-direction: column; gap: 8px; }
    .user { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 24px; padding: 12px 16px;
            background: var(--js-surface); border: 1px solid var(--js-border); border-radius: var(--js-radius); }
    .user.suspended { background: #f8f9fb; }
    .main { flex: 1 1 280px; min-width: 0; }
    .reason { font-size: 14px; margin-top: 4px; font-style: italic; }
    .cost { display: flex; flex-direction: column; font-variant-numeric: tabular-nums; font-size: 14px; min-width: 100px; }
    .chip { font-size: 12px; padding: 2px 8px; border-radius: 10px; background: var(--mat-sys-surface-container-high); }
    .chip[data-access='Approved'] { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .chip[data-access='Pending'] { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); }
    .chip[data-access='Suspended'] { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .invite { display: flex; flex-wrap: wrap; align-items: baseline; gap: 12px; }
    .note { flex: 1 1 280px; }
    .created { margin-bottom: 16px; }
    .link { display: flex; align-items: center; gap: 8px; }
    .provider { min-width: 80px; font-weight: 500; }
    .link code { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; }
    .invitations { display: flex; flex-direction: column; gap: 6px; }
    .clicks { color: var(--mat-sys-on-surface-variant); }
    .feedback { display: flex; flex-direction: column; gap: 8px; }
    .message { padding: 12px 16px; background: var(--js-surface); border: 1px solid var(--js-border); border-radius: var(--js-radius); }
    .message .text { margin: 8px 0 0; white-space: pre-line; }
    .stars { color: #e3a008; letter-spacing: 1px; }
    .reply { margin-left: auto; }
    .chip[data-kind='Problem'] { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .chip[data-kind='Praise'] { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .invitation { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
  `,
})
export class AdminPage {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly clipboard = inject(Clipboard);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly maxNote = MAX_INVITATION_NOTE;
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly users = signal<AdminUser[]>([]);
  protected readonly invitations = signal<Invitation[]>([]);
  protected readonly feedback = signal<FeedbackRow[]>([]);
  protected readonly feedbackError = signal<string | null>(null);
  protected readonly note = signal('');
  protected readonly created = signal<{ invitation: CreatedInvitation; links: InvitationLink[] } | null>(null);

  /** Waiting = Pending AND actually asked (signing in alone is not asking). */
  protected readonly requests = computed(() => this.users().filter(u => u.access === 'Pending' && u.accessRequestedAt));
  protected readonly others = computed(() => this.users().filter(u => !(u.access === 'Pending' && u.accessRequestedAt)));
  protected readonly total7 = computed(() => this.users().reduce((sum, u) => sum + u.costLast7DaysUsd, 0));
  protected readonly total30 = computed(() => this.users().reduce((sum, u) => sum + u.costLast30DaysUsd, 0));

  protected readonly usd = formatUsd;
  protected readonly t = t;
  protected readonly lang = lang;

  constructor() {
    void this.reload();
    void this.loadFeedback();
  }

  protected stars(rating: number): string {
    return '★'.repeat(rating) + '☆'.repeat(5 - rating);
  }

  /** "Odpovědět" opens the admin's mail client with the user's message quoted. */
  protected mailto(f: FeedbackRow): string {
    const subject = `Re: Job Scout – ${t().feedback.kinds[f.kind]}`;
    const body = `\n\n> ${f.message.replace(/\n/g, '\n> ')}`;
    return `mailto:${f.email ?? ''}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
  }

  private async loadFeedback(): Promise<void> {
    try {
      this.feedback.set(await this.admin.feedback());
    } catch (e) {
      this.feedbackError.set(problemMessage(e, t().admin.feedbackFailed));
    }
  }

  protected accessLabel(access: AccessStatus): string {
    return t().admin.access[access];
  }

  /** The admin can't suspend themselves – they would lose AI on their own account (admin rights come from configuration). */
  protected isMe(u: AdminUser): boolean {
    return this.auth.me()?.id === u.id;
  }

  protected async approve(u: AdminUser): Promise<void> {
    const ok = await this.confirm.ask({
      title: u.access === 'Suspended' ? t().admin.restoreTitle : t().admin.approveTitle,
      message: t().admin.approveText(u.displayName, u.email),
      confirm: u.access === 'Suspended' ? t().admin.restoreConfirm : t().admin.approveConfirm,
      cancel: t().common.cancel,
    });
    if (ok) await this.act(() => this.admin.approve(u.id), t().admin.approved(u.displayName));
  }

  protected async suspend(u: AdminUser): Promise<void> {
    const ref = this.dialog.open<SuspendDialog, SuspendData, string | null>(SuspendDialog, {
      data: { displayName: u.displayName }, width: '520px',
    });
    const reason = await firstValueFrom(ref.afterClosed());
    if (reason) await this.act(() => this.admin.suspend(u.id, reason), t().admin.suspended(u.displayName));
  }

  protected async reject(u: AdminUser): Promise<void> {
    const ref = this.dialog.open<SuspendDialog, SuspendData, string | null>(SuspendDialog, {
      data: { displayName: u.displayName, mode: 'reject' }, width: '520px',
    });
    const reason = await firstValueFrom(ref.afterClosed());
    if (reason) await this.act(() => this.admin.reject(u.id, reason), t().admin.rejectedDone(u.displayName));
  }

  protected async prepareDemo(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.admin.prepareDemo();
      this.toast(t().admin.demoPrepared);
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e, t().admin.demoFailed));
    } finally {
      this.busy.set(false);
    }
  }

  protected async createInvitation(): Promise<void> {
    const note = this.note().trim();
    if (!note || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const [invitation, providers] = await Promise.all([this.admin.createInvitation(note), this.auth.providers()]);
      this.created.set({ invitation, links: invitationLinks(invitation.code, location.origin, providers.external) });
      this.note.set('');
      this.invitations.set(await this.admin.invitations());
    } catch (e) {
      this.error.set(problemMessage(e, t().admin.inviteFailed));
    } finally {
      this.busy.set(false);
    }
  }

  protected copy(link: InvitationLink): void {
    this.toast(this.clipboard.copy(link.url) ? t().admin.linkCopied(link.label) : t().admin.linkCopyFailed);
  }

  private async act(action: () => Promise<void>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.toast(done);
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e, t().common.actionFailed));
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    try {
      const [users, invitations] = await Promise.all([this.admin.users(), this.admin.invitations()]);
      this.users.set(users);
      this.invitations.set(invitations);
    } catch (e) {
      this.error.set(problemMessage(e, t().admin.loadFailed));
    } finally {
      this.loading.set(false);
    }
  }

  private toast(message: string): void {
    this.snackBar.open(message, undefined, { duration: 3000 });
  }
}
