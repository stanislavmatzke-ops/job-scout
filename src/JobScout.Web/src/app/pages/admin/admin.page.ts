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
import { AdminUser, CreatedInvitation, Invitation, InvitationLink } from '../../core/admin.models';
import { AccessStatus } from '../../core/models';
import { problemMessage } from '../../core/match-format';
import { ConfirmService } from '../../shared/confirm-dialog';
import { SuspendDialog, SuspendData } from './suspend-dialog';

const ACCESS_LABELS: Record<AccessStatus, string> = {
  Pending: 'Čeká',
  Approved: 'Aktivní',
  Suspended: 'Pozastavený',
};

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
      <h1>Správa</h1>
      <p>Žádosti o přístup, uživatelé a jejich náklady na AI, pozvánky.</p>
    </header>
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
    @if (error(); as e) { <p class="error" role="alert">{{ e }}</p> }

    <!-- ───── requests ───── -->
    <section>
      <h2>Žádosti o přístup @if (requests().length) { <span class="count">{{ requests().length }}</span> }</h2>
      @for (u of requests(); track u.id) {
        <mat-card appearance="outlined" class="request">
          <mat-card-content>
            <div class="who"><strong>{{ u.displayName }}</strong> <span class="muted">{{ u.email }} · {{ u.provider }}</span></div>
            <div class="muted">požádal(a) {{ u.accessRequestedAt | date: 'd. M. y H:mm' }}</div>
            @if (u.accessRequestMessage) { <blockquote>{{ u.accessRequestMessage }}</blockquote> }
            @else { <p class="muted">Bez zprávy.</p> }
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-button [disabled]="busy()" (click)="suspend(u)">Zamítnout</button>
            <button mat-flat-button [disabled]="busy()" (click)="approve(u)">Schválit</button>
          </mat-card-actions>
        </mat-card>
      } @empty {
        @if (!loading()) { <p class="muted">Nikdo nečeká na schválení.</p> }
      }
    </section>

    <!-- ───── users ───── -->
    <section>
      <h2>Uživatelé</h2>
      @if (users().length) {
        <p class="muted totals">
          Náklady na AI celkem: 7 dní <strong>{{ usd(total7()) }}</strong> · 30 dní <strong>{{ usd(total30()) }}</strong>
        </p>
      }
      <div class="users">
        @for (u of others(); track u.id) {
          <div class="user" [class.suspended]="u.access === 'Suspended'">
            <div class="main">
              <div class="who">
                <strong>{{ u.displayName }}</strong>
                <span class="chip" [attr.data-access]="u.access">{{ accessLabel(u.access) }}</span>
                @if (isMe(u)) { <span class="chip">vy</span> }
              </div>
              <div class="muted">{{ u.email }} · {{ u.provider }}</div>
              <div class="muted">
                @if (u.lastSignInAt) { naposledy {{ u.lastSignInAt | date: 'd. M. y H:mm' }} } @else { zatím nepřihlášen(a) }
                · {{ u.hasProfile ? 'má profil' : 'bez profilu' }}
              </div>
              @if (u.suspendedReason) { <div class="reason">Důvod: {{ u.suspendedReason }}</div> }
            </div>
            <div class="cost" title="Náklady na AI za 7 / 30 dní">
              <span>7 d: {{ usd(u.costLast7DaysUsd) }}</span>
              <span>30 d: {{ usd(u.costLast30DaysUsd) }}</span>
            </div>
            <div class="actions">
              @switch (u.access) {
                @case ('Approved') {
                  @if (!isMe(u)) { <button mat-stroked-button [disabled]="busy()" (click)="suspend(u)">Pozastavit</button> }
                }
                @case ('Suspended') { <button mat-stroked-button [disabled]="busy()" (click)="approve(u)">Obnovit přístup</button> }
                @case ('Pending') { <button mat-stroked-button [disabled]="busy()" (click)="approve(u)">Schválit</button> }
              }
            </div>
          </div>
        }
      </div>
    </section>

    <!-- ───── invitations ───── -->
    <section>
      <h2>Pozvánky</h2>
      <p class="muted">Kdo se přihlásí přes pozvánku, má přístup hned, bez schvalování. Pozvánka platí 14 dní a jen pro jedno přihlášení.</p>
      <form class="invite" (ngSubmit)="createInvitation()">
        <mat-form-field appearance="outline" class="note">
          <mat-label>Pro koho (uvidíte jen vy)</mat-label>
          <input matInput name="note" [maxlength]="maxNote" [ngModel]="note()" (ngModelChange)="note.set($event)" />
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy() || !note().trim()">Vytvořit pozvánku</button>
      </form>

      @if (created(); as c) {
        <mat-card appearance="outlined" class="created">
          <mat-card-content>
            <p><mat-icon inline>info</mat-icon>
              <strong>Odkaz si zkopírujte hned</strong> – později už ho nezobrazíme (ukládáme jen jeho otisk).
              Platí do {{ c.invitation.expiresAt | date: 'd. M. y' }}.</p>
            @for (link of c.links; track link.provider) {
              <div class="link">
                <span class="provider">{{ link.label }}</span>
                <code>{{ link.url }}</code>
                <button mat-icon-button (click)="copy(link)" [attr.aria-label]="'Zkopírovat odkaz ' + link.label">
                  <mat-icon>content_copy</mat-icon>
                </button>
              </div>
            } @empty {
              <p>Není nastavené žádné přihlášení přes Microsoft ani Google – kód pozvánky: <code>{{ c.invitation.code }}</code></p>
            }
          </mat-card-content>
        </mat-card>
      }

      <div class="invitations">
        @for (i of invitations(); track i.id) {
          <div class="invitation">
            <strong>{{ i.note || '(bez poznámky)' }}</strong>
            <span class="muted">vytvořena {{ i.createdAt | date: 'd. M. y' }}</span>
            @if (i.usedAt) {
              <span class="chip" data-access="Approved">použil(a) {{ i.usedBy }} {{ i.usedAt | date: 'd. M. y' }}</span>
            } @else if (i.isUsable) {
              <span class="chip">platí do {{ i.expiresAt | date: 'd. M. y' }}</span>
            } @else {
              <span class="chip" data-access="Suspended">vypršela</span>
            }
          </div>
        } @empty {
          @if (!loading()) { <p class="muted">Zatím žádné pozvánky.</p> }
        }
      </div>
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
  protected readonly note = signal('');
  protected readonly created = signal<{ invitation: CreatedInvitation; links: InvitationLink[] } | null>(null);

  /** Waiting = Pending AND actually asked (signing in alone is not asking). */
  protected readonly requests = computed(() => this.users().filter(u => u.access === 'Pending' && u.accessRequestedAt));
  protected readonly others = computed(() => this.users().filter(u => !(u.access === 'Pending' && u.accessRequestedAt)));
  protected readonly total7 = computed(() => this.users().reduce((sum, u) => sum + u.costLast7DaysUsd, 0));
  protected readonly total30 = computed(() => this.users().reduce((sum, u) => sum + u.costLast30DaysUsd, 0));

  protected readonly usd = formatUsd;

  constructor() {
    void this.reload();
  }

  protected accessLabel(access: AccessStatus): string {
    return ACCESS_LABELS[access];
  }

  /** The admin can't suspend themselves – they would lose AI on their own account (admin rights come from configuration). */
  protected isMe(u: AdminUser): boolean {
    return this.auth.me()?.id === u.id;
  }

  protected async approve(u: AdminUser): Promise<void> {
    const ok = await this.confirm.ask({
      title: u.access === 'Suspended' ? 'Obnovit přístup?' : 'Schválit přístup?',
      message: `${u.displayName} (${u.email}) bude moci nahrát životopis a každé ráno dostávat nabídky. To znamená náklady na AI.`,
      confirm: u.access === 'Suspended' ? 'Obnovit' : 'Schválit',
    });
    if (ok) await this.act(() => this.admin.approve(u.id), `${u.displayName} má přístup.`);
  }

  protected async suspend(u: AdminUser): Promise<void> {
    const ref = this.dialog.open<SuspendDialog, SuspendData, string | null>(SuspendDialog, {
      data: { displayName: u.displayName }, width: '520px',
    });
    const reason = await firstValueFrom(ref.afterClosed());
    if (reason) await this.act(() => this.admin.suspend(u.id, reason), `Účet ${u.displayName} je pozastaven.`);
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
      this.error.set(problemMessage(e, 'Pozvánku se nepodařilo vytvořit.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected copy(link: InvitationLink): void {
    this.toast(this.clipboard.copy(link.url) ? `Odkaz (${link.label}) je ve schránce.` : 'Odkaz se nepodařilo zkopírovat.');
  }

  private async act(action: () => Promise<void>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.toast(done);
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e, 'Akce se nezdařila. Zkuste to prosím znovu.'));
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
      this.error.set(problemMessage(e, 'Přehled se nepodařilo načíst.'));
    } finally {
      this.loading.set(false);
    }
  }

  private toast(message: string): void {
    this.snackBar.open(message, undefined, { duration: 3000 });
  }
}
