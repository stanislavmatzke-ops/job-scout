import { firstValueFrom } from 'rxjs';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { DateAdapter, provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatTabsModule } from '@angular/material/tabs';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { FormsModule } from '@angular/forms';
import { MatchesService } from '../../core/matches.service';
import { MatchList, MatchListItem, MatchStatus, MatchView } from '../../core/matches.models';
import { STATUS_OPTIONS, VIEWS, parseView, problemMessage, scoreTone, statusLabel } from '../../core/match-format';
import { ConfirmService } from '../../shared/confirm-dialog';
import { AuthService } from '../../core/auth.service';
import { AddJobDialog, AddJobOutcome } from './add-job-dialog';
import { SCOUT_POLL_MS, ScoutService, ScoutStatus } from '../../core/scout.service';
import { lang, t } from '../../core/i18n';
import { MatchSort, arrangeByDay, dayDate, dayHeading, dayKey, dayLabel, parseDay, parseSort } from '../../core/match-days';

@Component({
  selector: 'app-matches-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink, DatePipe, FormsModule, MatTabsModule, MatCardModule, MatIconModule, MatButtonModule, MatProgressBarModule,
    MatTooltipModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatDatepickerModule,
    MatButtonToggleModule,
  ],
  providers: [provideNativeDateAdapter()],
  template: `
    <header class="page-head">
      <div class="title-row">
        <h1>{{ t().matches.title }}</h1>
        @if (aiEnabled()) {
          <button mat-flat-button (click)="addJob()"><mat-icon>add</mat-icon> {{ t().matches.add }}</button>
        }
      </div>
      <p>{{ t().matches.intro1 }}<strong>{{ t().matches.introStrong }}</strong>{{ t().matches.intro2 }}</p>
    </header>
    @if (scout(); as s) {
      @if (s.searching) {
        <mat-card appearance="outlined" class="searching" role="status">
          <mat-progress-bar mode="indeterminate" />
          <mat-card-content>
            <strong>{{ t().matches.searchingTitle }}</strong>
            {{ t().matches.searchingText }}
          </mat-card-content>
        </mat-card>
      } @else {
        <p class="schedule">
          {{ t().matches.schedule }}
          @if (s.lastSearchAt) { {{ t().matches.last((s.lastSearchAt | date: t().fmt.dateTime : undefined : lang()) ?? '') }} }
        </p>
      }
    }

    <nav mat-tab-nav-bar [tabPanel]="panel">
      @for (tab of tabs; track tab.view) {
        <a mat-tab-link [routerLink]="[]" [queryParams]="{ view: tab.param }" queryParamsHandling="merge" [active]="current() === tab.view">
          {{ tab.label }}
          @if (list(); as l) { <span class="count">{{ l.counts[tab.countKey] }}</span> }
        </a>
      }
    </nav>

    <mat-tab-nav-panel #panel>
      @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
      @if (failed()) { <p class="error">{{ t().matches.loadFailed }}</p> }

      @if (list(); as l) {
        @if (l.items.length) {
          <!-- ===== Which day, in which order ===== -->
          <div class="filters">
            <mat-button-toggle-group [value]="sort()" (change)="setSort($event.value)" hideSingleSelectionIndicator
                                     [attr.aria-label]="t().matches.sortLabel">
              <mat-button-toggle value="default">{{ current() === 'Open' || current() === 'Saved' ? t().matches.sortScore : t().matches.sortActivity }}</mat-button-toggle>
              <mat-button-toggle value="day">{{ t().matches.sortDay }}</mat-button-toggle>
            </mat-button-toggle-group>
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="day-field">
              <mat-label>{{ t().matches.day }}</mat-label>
              <input matInput readonly [matDatepicker]="picker" [value]="dayValue()" [placeholder]="t().matches.allDays"
                     (dateChange)="setDay($event.value)" (click)="picker.open()" />
              <mat-datepicker-toggle matIconSuffix [for]="picker" [attr.aria-label]="t().matches.pickDay" />
              <mat-datepicker #picker [dateClass]="dateClass" />
            </mat-form-field>
            <button mat-button (click)="setDay(today())" [disabled]="day() === today()">{{ t().matches.today }}</button>
            @if (day()) {
              <button mat-button (click)="setDay(null)"><mat-icon>close</mat-icon> {{ t().matches.allDays }}</button>
              <span class="shown">{{ t().matches.shownOf(shownCount(), l.items.length) }}</span>
            }
          </div>
        }
        @if (shownCount()) {
          <!-- ===== Bulk action: one status for the selected jobs ===== -->
          <div class="bulk" [class.active]="selected().size > 0">
            <mat-checkbox [checked]="allSelected()" [indeterminate]="selected().size > 0 && !allSelected()"
                          (change)="toggleAll()" [attr.aria-label]="t().matches.selectAll">
              {{ selected().size ? t().matches.selected(selected().size) : t().matches.selectAll }}
            </mat-checkbox>
            @if (selected().size) {
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="bulk-status">
                <mat-label>{{ t().matches.bulkStatus }}</mat-label>
                <mat-select [ngModel]="bulkStatus()" (ngModelChange)="bulkStatus.set($event)" name="bulkStatus">
                  @for (s of statuses; track s.value) { <mat-option [value]="s.value">{{ s.label }}</mat-option> }
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="bulk-note">
                <mat-label>{{ t().matches.bulkNote }}</mat-label>
                <input matInput name="bulkNote" maxlength="2000" [(ngModel)]="bulkNote" />
              </mat-form-field>
              <button mat-flat-button (click)="applyBulk()" [disabled]="!bulkStatus() || saving()">{{ t().matches.bulkApply }}</button>
              <button mat-button (click)="clearSelection()" [disabled]="saving()">{{ t().matches.clearSelection }}</button>
            }
          </div>
        }
        <div class="items">
          @for (group of groups(); track group.key) {
          @if (sort() === 'day') {
            <h2 class="day-heading">{{ heading(group.key) }} <span class="count">{{ t().matches.dayCount(group.items.length) }}</span></h2>
          }
          @for (item of group.items; track item.id) {
            <mat-card appearance="outlined" class="item" [class.picked]="selected().has(item.id)">
              <mat-checkbox class="pick" [checked]="selected().has(item.id)" (change)="toggle(item.id)"
                            [attr.aria-label]="t().matches.select(item.title)" />
              <div class="score {{ tone(item) }}" [matTooltip]="scoreHint(item)" [attr.aria-label]="scoreHint(item)">
                @if (item.score !== null) {
                  <span class="value">{{ item.score }}<small>/10</small></span><span class="caption">{{ t().score.match }}</span>
                } @else {
                  <span class="value">–</span><span class="caption">{{ t().score.waiting }}</span>
                }
              </div>
              <div class="body">
                <a class="title" [routerLink]="['/matches', item.id]">{{ item.title }}</a>
                @if (item.addedManually) { <span class="manual" [matTooltip]="t().matches.manualHint">{{ t().matches.manual }}</span> }
                <div class="meta">{{ meta(item) }}</div>
                @if (item.summary) {
                  <p class="summary"><mat-icon class="ai-mark" [matTooltip]="t().matches.aiSummary">auto_awesome</mat-icon> {{ item.summary }}</p>
                } @else if (item.triageReason) {
                  <p class="summary muted">{{ item.triageReason }}</p>
                } @else if (item.stage === 'Shortlisted' && item.addedManually) {
                  <p class="summary muted">{{ t().matches.waitingManual }}</p>
                } @else if (item.stage === 'Shortlisted') {
                  <p class="summary muted">{{ t().matches.waitingEval(item.triageScore) }}</p>
                }
              </div>
              <div class="side">
                @if (item.evaluationOutdated) { <span class="outdated" [matTooltip]="t().matches.outdatedHint">{{ t().matches.outdated }}</span> }
                @if (item.status !== 'New') { <span class="status">{{ statusLabel(item.status) }}</span> }
                @if (item.hasCoverLetter) { <mat-icon [matTooltip]="t().matches.letterReady">mail</mat-icon> }
                @if (item.url) {
                  <a mat-icon-button [href]="item.url" target="_blank" rel="noopener" [matTooltip]="t().matches.openAd" [attr.aria-label]="t().matches.openAd">
                    <mat-icon>open_in_new</mat-icon>
                  </a>
                }
              </div>
            </mat-card>
          }
          } @empty {
            <p class="empty">
              {{ day() && l.items.length ? t().matches.dayEmpty : emptyText() }}
              @if (isEmptyAccount(l)) {
                <br />{{ t().matches.noCv }} <a routerLink="/profile">{{ t().matches.startProfile }}</a>
              }
            </p>
          }
        </div>
      }
    </mat-tab-nav-panel>
  `,
  styles: `
    .title-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .manual { margin-left: 8px; font-size: 0.75em; padding: 1px 8px; border-radius: 8px; vertical-align: 2px;
              border: 1px solid var(--mat-sys-outline-variant); color: var(--js-muted); }
    .searching { margin-bottom: 16px; overflow: hidden; border-color: var(--js-ai-border); background: var(--js-ai-tint); }
    .searching mat-card-content { padding-top: 12px; }
    .schedule { margin: -8px 0 16px; color: var(--js-muted); font-size: 0.9em; }
    .count { margin-left: 6px; padding: 0 8px; border-radius: 10px; background: var(--mat-sys-surface-container-high); font-size: 0.8em; }
    .items { display: flex; flex-direction: column; gap: 12px; padding-top: 16px; }
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding-top: 16px; }
    .day-field { width: 190px; }
    .day-field input { cursor: pointer; }
    .shown { color: var(--js-muted); font-size: 0.9em; }
    .day-heading { margin: 12px 0 0; font-size: 1.05rem; font-weight: 600; }
    .day-heading:first-child { margin-top: 0; }
    .bulk { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding: 12px 4px 0; }
    .bulk.active { padding: 8px 12px; margin-top: 12px; border-radius: 12px; background: var(--mat-sys-surface-container-high); }
    .bulk-status { width: 220px; }
    .bulk-note { flex: 1; min-width: 200px; }
    .item.picked { border-color: var(--mat-sys-primary); }
    .pick { margin: 4px -8px 0 -6px; }
    .item { display: flex; flex-direction: row; align-items: flex-start; gap: 16px; padding: 16px 16px 16px 14px; }
    .body { flex: 1; min-width: 0; }
    .title { font-weight: 600; font-size: 1.05rem; text-decoration: none; color: var(--js-text); }
    .title:hover { text-decoration: underline; }
    .meta { color: var(--js-muted); font-size: 0.9em; margin-top: 2px; }
    .summary { margin: 8px 0 0; line-height: 1.5; max-width: 80ch; }
    .side { display: flex; align-items: center; gap: 4px; }
    .status { font-size: 0.8em; padding: 2px 8px; border-radius: 8px; background: var(--mat-sys-secondary-container); }
    .outdated { font-size: 0.75em; padding: 2px 8px; border-radius: 8px; border: 1px dashed var(--mat-sys-outline); opacity: 0.8; }
    .empty { padding: 32px 0; text-align: center; opacity: 0.7; }
    @media (max-width: 600px) { .item { flex-wrap: wrap; } .side { width: 100%; justify-content: flex-end; } }
  `,
})
export class MatchesPage {
  private readonly matches = inject(MatchesService);
  private readonly scoutService = inject(ScoutService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  /** ?view=… from the URL (component input binding) – tabs are links, so Back and bookmarks work. */
  readonly view = input<string>();
  /** ?day=2026-10-15 – only the jobs found that day; ?sort=day – grouped by day. Kept across tabs. */
  readonly dayParam = input<string>(undefined, { alias: 'day' });
  readonly sortParam = input<string>(undefined, { alias: 'sort' });

  protected readonly tabs = VIEWS;
  protected readonly current = computed(() => parseView(this.view()));
  protected readonly emptyText = computed(() => VIEWS.find(v => v.view === this.current())!.empty);
  protected readonly list = signal<MatchList | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly scout = signal<ScoutStatus | null>(null);

  // ---------- which day, in which order (all on the loaded tab – the API sends the whole tab) ----------
  protected readonly day = computed(() => parseDay(this.dayParam()));
  protected readonly sort = computed<MatchSort>(() => parseSort(this.sortParam()));
  protected readonly dayValue = computed(() => { const d = this.day(); return d ? dayDate(d) : null; });
  protected readonly today = signal(dayKey(new Date()));
  protected readonly groups = computed(() => arrangeByDay(this.list()?.items ?? [], this.day(), this.sort()));
  protected readonly shown = computed(() => this.groups().flatMap(g => g.items));
  protected readonly shownCount = computed(() => this.shown().length);
  /** Days that have jobs in this tab are bold in the calendar. */
  private readonly daysWithJobs = computed(() => new Set((this.list()?.items ?? []).map(i => dayKey(i.foundAt))));
  protected readonly dateClass = (date: Date) => this.daysWithJobs().has(dayKey(date)) ? 'has-jobs' : '';

  /** Ids picked for a bulk action – only within the current tab, emptied whenever the list reloads. */
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly allSelected = computed(() => {
    const items = this.shown();                             // "all" = what is on the screen, not the hidden days
    return items.length > 0 && items.every(i => this.selected().has(i.id));
  });
  protected readonly saving = signal(false);
  protected readonly bulkStatus = signal<MatchStatus | null>(null);
  protected bulkNote = '';
  protected readonly statuses = STATUS_OPTIONS;

  /** Adding a job calls the AI – Approved users only, never the demo (ADR-0017, ADR-0020); the API enforces it too. */
  protected readonly aiEnabled = computed(() => this.auth.me()?.access === 'Approved' && !this.auth.me()?.isDemo);

  protected readonly statusLabel = statusLabel;
  protected readonly t = t;
  protected readonly lang = lang;

  constructor() {
    // Reload whenever the tab in the URL changes
    effect(() => {
      const view = this.current();
      untracked(() => this.load(view));
    });
    // Another day = other jobs on the screen – a selection must never include jobs the user can't see
    effect(() => {
      this.day();
      untracked(() => this.selected.set(new Set()));
    });
    // The calendar speaks the user's language (month names, first day of the week)
    const dates = inject(DateAdapter);
    effect(() => dates.setLocale(lang()));
    void this.checkScout();
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));
  }

  /**
   * While the first search runs (6h), ask again every few seconds; when it ends, reload the list.
   * The status is only a hint – if it can't be loaded, the page works without it.
   */
  private async checkScout(): Promise<void> {
    const wasSearching = this.scout()?.searching ?? false;
    try {
      const status = await this.scoutService.status();
      this.scout.set(status);
      if (status.searching)
        this.pollTimer = setTimeout(() => void this.checkScout(), SCOUT_POLL_MS);
      else if (wasSearching)
        await this.load(this.current());
    } catch {
      // no status → no banner; the jobs still show
    }
  }

  /** No jobs in any tab – most likely a new user without a CV. */
  protected isEmptyAccount(list: MatchList): boolean {
    const c = list.counts;
    return c.open + c.saved + c.inProgress + c.closed + c.triagedOut === 0;
  }

  // ---------- a job from elsewhere (ADR-0013) ----------

  protected async addJob(): Promise<void> {
    const ref = this.dialog.open<AddJobDialog, void, AddJobOutcome>(AddJobDialog,
      { width: '680px', maxWidth: '95vw', autoFocus: 'first-tabbable' });
    const result = await firstValueFrom(ref.afterClosed());
    if (!result) return;

    const message = result.alreadyInList ? t().addJob.alreadyInList
      : result.evaluateRequested && !result.evaluated ? t().addJob.addedNotEvaluated
      : t().addJob.added;
    this.snackBar.open(message, undefined, { duration: 5000 });
    await this.router.navigate(['/matches', result.id]);
  }

  /** "Firma · Praha · nalezeno dnes" – an unknown location of an ad added by hand is left out. */
  protected meta(item: MatchListItem): string {
    this.today();                                           // relative words follow the day the page was opened
    return [item.company, item.location, t().matches.found(dayLabel(dayKey(item.foundAt)))].filter(Boolean).join(' · ');
  }

  protected heading(key: string): string {
    return dayHeading(key);
  }

  protected setDay(value: Date | string | null): void {
    const day = value instanceof Date ? dayKey(value) : value;
    this.today.set(dayKey(new Date()));                     // the page may have been open since yesterday
    void this.router.navigate([], { queryParams: { day }, queryParamsHandling: 'merge' });
  }

  protected setSort(value: MatchSort): void {
    void this.router.navigate([], { queryParams: { sort: value === 'day' ? 'day' : null }, queryParamsHandling: 'merge' });
  }

  // ---------- bulk action ----------

  protected toggle(id: string): void {
    const next = new Set(this.selected());
    if (!next.delete(id)) next.add(id);
    this.selected.set(next);
  }

  protected toggleAll(): void {
    this.selected.set(this.allSelected() ? new Set() : new Set(this.shown().map(i => i.id)));
  }

  protected clearSelection(): void {
    this.selected.set(new Set());
  }

  protected async applyBulk(): Promise<void> {
    const status = this.bulkStatus();
    const ids = [...this.selected()];
    if (!status || ids.length === 0 || this.saving()) return;
    if (!(await this.confirm.ask({
      title: t().matches.bulkConfirmTitle,
      message: t().matches.bulkConfirmText(ids.length, statusLabel(status)),
      confirm: t().matches.bulkApply,
    }))) {
      return;
    }

    this.saving.set(true);
    try {
      const { changed } = await this.matches.changeStatusMany(ids, status, this.bulkNote.trim() || null);
      this.snackBar.open(t().matches.bulkDone(changed), undefined, { duration: 3000 });
      this.bulkStatus.set(null);
      this.bulkNote = '';
      await this.load(this.current());                      // the jobs usually move to another tab
    } catch (error) {
      this.snackBar.open(problemMessage(error, t().common.saveFailed), undefined, { duration: 6000 });
    } finally {
      this.saving.set(false);
    }
  }

  protected tone(item: MatchListItem) {
    return scoreTone(item.score);
  }

  protected scoreHint(item: MatchListItem): string {
    return item.score === null ? t().score.notEvaluated : t().score.hint(item.score);
  }

  private async load(view: MatchView): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.list.set(await this.matches.list(view));
      this.selected.set(new Set());
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
