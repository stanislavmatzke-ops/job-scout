import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth.service';
import { ScoutService, ScoutStatus } from '../../core/scout.service';
import { ProfileService, cvProblem } from '../../core/profile.service';
import { GrammaticalGender, ProfileView, Seniority } from '../../core/profile.models';
import { aiErrorMessage, employmentLabel, problemMessage, remoteLabel } from '../../core/match-format';
import { lang, t } from '../../core/i18n';

type Work = 'cv' | 'notes';

@Component({
  selector: 'app-profile-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, DecimalPipe, RouterLink, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatRadioModule, MatChipsModule, MatProgressBarModule, MatSlideToggleModule,
  ],
  template: `
    <header class="page-head">
      <h1>{{ t().profile.title }}</h1>
      <p>{{ t().profile.intro }}</p>
    </header>

    @if (loadError()) { <p class="error">{{ t().profile.loadFailed }}</p> }

    @if (view(); as v) {
      @if (!aiEnabled() && !isDemo()) {
        <p class="ai-off">{{ t().profile.aiOff }}</p>
      }

      @if (canStartFirstSearch()) {
        <div class="start-card" role="region" [attr.aria-label]="t().profile.startTitle2">
          <div>
            <h2>{{ t().profile.startTitle2 }}</h2>
            <p>{{ t().profile.startText2 }}</p>
          </div>
          <button mat-flat-button (click)="startFirstSearch()" [disabled]="!aiEnabled() || starting() || working() !== null">
            <mat-icon>travel_explore</mat-icon> {{ t().profile.startButton }}
          </button>
        </div>
      }

      @if (firstSearch()) {
        <p class="info" role="status">
          <mat-icon>travel_explore</mat-icon>
          <span>{{ t().profile.firstSearch1 }}<a routerLink="/matches">{{ t().profile.firstSearchLink }}</a>{{ t().profile.firstSearch2 }}</span>
        </p>
      }

      @if (v.outdatedEvaluations > 0) {
        <p class="info">
          <mat-icon>info</mat-icon>
          <span>{{ t().profile.outdated1 }}<strong>{{ v.outdatedEvaluations }}</strong>{{ t().profile.outdated2 }}<a routerLink="/matches">{{ t().profile.outdatedLink }}</a>{{ t().profile.outdated3 }}</span>
        </p>
      }

      <div class="grid">
        <section class="column">
          <!-- ===== CV ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>{{ t().profile.cv }}</mat-card-title></mat-card-header>
            @if (working() === 'cv') { <mat-progress-bar mode="indeterminate" /> }
            <mat-card-content>
              @if (working() === 'cv') {
                <p class="muted">{{ t().profile.analyzing }}</p>
              } @else if (v.version) {
                <p>
                  <mat-icon class="inline">description</mat-icon> <strong>{{ v.cvFileName }}</strong><br />
                  <span class="muted">{{ t().profile.version(v.version, (v.analyzedAt | date: t().fmt.dateTime : undefined : lang()) ?? '') }}</span>
                </p>
                @if (!v.hasCv) {
                  <p class="info">
                    <mat-icon>info</mat-icon>
                    <span>{{ t().profile.noStoredCv }}</span>
                  </p>
                }
              } @else {
                <h3>{{ t().profile.startTitle }}</h3>
                <p>{{ t().profile.startText }}</p>
              }
              @if (fileError(); as message) { <p class="error">{{ message }}</p> }
              <p class="muted small">{{ t().profile.formats }}</p>
            </mat-card-content>
            <mat-card-actions>
              <input #file type="file" hidden accept=".pdf,.md,.txt" (change)="onFile(file)" />
              <button mat-flat-button (click)="file.click()" [disabled]="!aiEnabled() || working() !== null">
                <mat-icon>upload_file</mat-icon> {{ v.hasCv ? t().profile.uploadNew : t().profile.upload }}
              </button>
            </mat-card-actions>
          </mat-card>

          <!-- ===== Notes ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>{{ t().profile.notesTitle }}</mat-card-title></mat-card-header>
            @if (working() === 'notes') { <mat-progress-bar mode="indeterminate" /> }
            <mat-card-content>
              <p class="muted small">{{ t().profile.notesHelp }}</p>
              <mat-form-field appearance="outline" class="full">
                <mat-label>{{ t().profile.notes }}</mat-label>
                <textarea matInput rows="7" name="notes" maxlength="4000" [(ngModel)]="notes"
                          [disabled]="!v.hasCv || working() !== null"></textarea>
                @if (!v.hasCv) {
                  <mat-hint>{{ t().profile.notesNeedCv(!!v.version) }}</mat-hint>
                }
              </mat-form-field>
            </mat-card-content>
            <mat-card-actions>
              <button mat-stroked-button (click)="saveNotes()"
                      [disabled]="!v.hasCv || !aiEnabled() || working() !== null || notes.trim() === (v.notes ?? '')">
                {{ t().profile.saveNotes }}
              </button>
            </mat-card-actions>
          </mat-card>

          <!-- ===== Gender ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>{{ t().profile.genderTitle }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <p class="muted small">{{ t().profile.genderHelp }}</p>
              <mat-radio-group class="radios" [ngModel]="v.gender" (ngModelChange)="saveGender($event)" name="gender" [disabled]="isDemo()">
                <mat-radio-button value="Masculine">{{ t().profile.masculine }}</mat-radio-button>
                <mat-radio-button value="Feminine">{{ t().profile.feminine }}</mat-radio-button>
                <mat-radio-button value="Unspecified">{{ t().profile.unspecified }}</mat-radio-button>
              </mat-radio-group>
            </mat-card-content>
          </mat-card>

          <!-- ===== E-mails (7f) ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>{{ t().profile.emailTitle }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <mat-slide-toggle [checked]="v.morningEmail ?? true" (change)="saveMorningEmail($event.checked)" [disabled]="isDemo()">
                {{ t().profile.morningEmail }}
              </mat-slide-toggle>
              <p class="muted small">{{ t().profile.morningEmailHelp }}</p>
            </mat-card-content>
          </mat-card>
        </section>

        <!-- ===== What the AI made of it ===== -->
        <aside class="column">
          @if (v.profile; as p) {
            <mat-card appearance="outlined" class="ai-panel">
              <mat-card-header>
                <mat-card-title class="card-title-row">
                  <span>{{ t().profile.aiTitle }}</span>
                  <span class="ai-badge"><mat-icon>auto_awesome</mat-icon>{{ t().common.ai }}</span>
                </mat-card-title>
                <mat-card-subtitle>{{ t().profile.aiSub }}</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <p class="headline">{{ p.headline }}</p>
                <p class="muted">{{ t().profile.experience(seniority(p.seniority), p.yearsOfExperience) }}</p>
                <p class="muted small lang-note"><mat-icon class="ai-mark">translate</mat-icon> {{ t().profile.aiLanguageNote }}</p>

                <h3>{{ t().profile.targetRoles }}</h3>
                <mat-chip-set>@for (x of p.targetRoles; track x) { <mat-chip>{{ x }}</mat-chip> }</mat-chip-set>

                <h3>{{ t().profile.coreSkills }}</h3>
                <mat-chip-set>@for (x of p.coreSkills; track x) { <mat-chip>{{ x }}</mat-chip> }</mat-chip-set>

                @if (p.domains.length) {
                  <h3>{{ t().profile.domains }}</h3>
                  <p>{{ p.domains.join(', ') }}</p>
                }

                <h3>{{ t().profile.preferences }}</h3>
                <dl class="facts">
                  <dt>{{ t().profile.remote }}</dt><dd>{{ remoteLabel(p.preferences.remoteWork) }}</dd>
                  <dt>{{ t().profile.employment }}</dt>
                  <dd>{{ p.preferences.employmentTypes.length ? employmentList(p.preferences.employmentTypes) : t().common.notStated }}</dd>
                  <dt>{{ t().profile.minSalary }}</dt>
                  <dd>{{ p.preferences.minMonthlySalaryCzk ? (p.preferences.minMonthlySalaryCzk | number: '1.0-0' : lang()) + (lang() === 'cs' ? ' Kč' : ' CZK') : t().common.notStatedF }}</dd>
                  <dt>{{ t().profile.where }}</dt>
                  <dd>
                    {{ p.search.homeLocation || t().common.notStated }}@if (p.search.commuteRadiusKm) {<span> + {{ p.search.commuteRadiusKm }} km</span>}@if (p.search.extraLocations.length) {<span>{{ t().profile.also(p.search.extraLocations.join(', ')) }}</span>}
                  </dd>
                </dl>
                @if (p.preferences.priorities.length) {
                  <h3>{{ t().profile.priorities }}</h3>
                  <ol class="plain">@for (x of p.preferences.priorities; track $index) { <li>{{ x }}</li> }</ol>
                }
                @if (p.preferences.avoid.length) {
                  <h3>{{ t().profile.avoid }}</h3>
                  <ul class="plain">@for (x of p.preferences.avoid; track $index) { <li>{{ x }}</li> }</ul>
                }

                <h3>{{ t().profile.searchAs }}</h3>
                <p>{{ p.search.professions.join(', ') }}</p>
                <p class="muted small">{{ t().profile.mismatch }}</p>
              </mat-card-content>
            </mat-card>
          }
        </aside>
      </div>
    }
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; align-items: start; }
    .column { display: flex; flex-direction: column; gap: 16px; min-width: 0; }
    .full { width: 100%; }
    .small { font-size: 0.85em; }
    .inline { vertical-align: middle; }
    .ai-off, .info {
      display: flex; gap: 10px; align-items: flex-start; padding: 10px 14px; margin: 0 0 16px;
      border-radius: var(--js-radius); background: #eaf3fc; border: 1px solid #cfe2f6;
    }
    .info .mat-icon { color: var(--js-primary); flex-shrink: 0; }
    .ai-off { background: #fff7e6; border-color: #f3dfb0; }
    .headline { font-size: 1.2rem; font-weight: 600; line-height: 1.35; margin: 8px 0 4px; }
    h3 { margin: 16px 0 6px; font-size: 1rem; }
    .facts { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; }
    .facts dt { color: var(--js-muted); }
    .facts dd { margin: 0; }
    .plain { margin: 0; padding-left: 20px; }
    .radios { display: flex; flex-direction: column; gap: 4px; }
    .start-card {
      display: flex; flex-wrap: wrap; align-items: center; gap: 12px 24px; padding: 16px 20px; margin: 0 0 16px;
      border-radius: var(--js-radius); background: var(--js-surface); border: 1px solid var(--js-border);
      box-shadow: inset 3px 0 0 var(--js-primary);
    }
    .start-card > div { flex: 1 1 320px; }
    .start-card h2 { margin: 0 0 4px; font-size: 1.1rem; font-weight: 600; }
    .start-card p { margin: 0; color: var(--js-muted); }
    @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class ProfilePage implements OnInit {
  private readonly profiles = inject(ProfileService);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly scout = inject(ScoutService);

  protected readonly view = signal<ProfileView | null>(null);
  protected readonly loadError = signal(false);
  protected readonly working = signal<Work | null>(null);
  protected readonly fileError = signal<string | null>(null);
  /** The very first profile was just created → the server started the first search (6h). */
  protected readonly firstSearch = signal(false);
  protected readonly scoutStatus = signal<ScoutStatus | null>(null);
  protected readonly starting = signal(false);
  /** A profile, never searched, nothing running → the user starts the first search (after the notes). */
  protected readonly canStartFirstSearch = computed(() => {
    const s = this.scoutStatus();
    return !!this.view()?.version && !!s && !s.searching && !s.lastSearchAt && !this.firstSearch() && !this.isDemo();
  });
  protected readonly aiEnabled = computed(() => this.auth.me()?.access === 'Approved' && !this.auth.me()?.isDemo);
  protected readonly isDemo = computed(() => !!this.auth.me()?.isDemo);

  protected notes = '';

  protected readonly remoteLabel = remoteLabel;
  protected readonly seniority = (s: Seniority) => t().seniority[s] ?? s;
  protected readonly t = t;
  protected readonly lang = lang;
  protected readonly employmentList = (types: Parameters<typeof employmentLabel>[0][]) => types.map(employmentLabel).join(', ');

  async ngOnInit(): Promise<void> {
    try {
      this.show(await this.profiles.get());
    } catch {
      this.loadError.set(true);
      return;
    }
    await this.loadScoutStatus();
  }

  /** The first search runs only when the user says so – the notes are usually written after the CV. */
  protected async startFirstSearch(): Promise<void> {
    this.starting.set(true);
    try {
      await this.scout.startFirst();
      this.firstSearch.set(true);
    } catch (error) {
      this.toast(problemMessage(error, t().profile.startFailed), 6000);
      await this.loadScoutStatus();
    } finally {
      this.starting.set(false);
    }
  }

  private async loadScoutStatus(): Promise<void> {
    try {
      this.scoutStatus.set(await this.scout.status());
    } catch {
      this.scoutStatus.set(null);                         // only a hint – the page works without it
    }
  }

  protected async onFile(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';                                       // choosing the same file again fires (change) again
    if (!file) return;

    const problem = cvProblem(file);
    this.fileError.set(problem);
    if (problem) return;

    await this.analyze('cv', () => this.profiles.uploadCv(file));
  }

  protected async saveNotes(): Promise<void> {
    await this.analyze('notes', () => this.profiles.updateNotes(this.notes.trim() || null));
  }

  protected async saveMorningEmail(on: boolean): Promise<void> {
    try {
      await this.profiles.updateMorningEmail(on);
      this.view.update(v => (v ? { ...v, morningEmail: on } : v));
      this.toast(t().profile.settingsSaved);
    } catch (error) {
      this.toast(problemMessage(error, t().profile.settingsFailed));
    }
  }

  protected async saveGender(gender: GrammaticalGender): Promise<void> {
    try {
      await this.profiles.updateGender(gender);
      this.view.update(v => (v ? { ...v, gender } : v));
      this.toast(t().profile.settingsSaved);
    } catch (error) {
      this.toast(problemMessage(error, t().profile.settingsFailed));
    }
  }

  /** CV upload and notes both end in an AI analysis – unless nothing changed (the server compares a hash). */
  private async analyze(work: Work, call: () => Promise<ProfileView>): Promise<void> {
    if (this.working()) return;
    const before = this.view()?.version ?? null;
    this.working.set(work);
    try {
      const after = await call();
      this.show(after);
      if (before === null && after.version !== null) await this.loadScoutStatus();   // the first profile → offer the first search
      this.toast(after.version === before
        ? t().profile.unchanged
        : t().profile.evaluated);
    } catch (error) {
      this.toast(aiErrorMessage(error), 6000);
    } finally {
      this.working.set(null);
    }
  }

  private show(view: ProfileView): void {
    this.notes = view.notes ?? '';
    this.view.set(view);
  }

  private toast(message: string, duration = 4000): void {
    this.snackBar.open(message, undefined, { duration });
  }
}
