import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatchesService } from '../../core/matches.service';
import { MatchList, MatchListItem, MatchView } from '../../core/matches.models';
import { VIEWS, parseView, scoreTone, statusLabel } from '../../core/match-format';

@Component({
  selector: 'app-matches-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, MatTabsModule, MatCardModule, MatIconModule, MatButtonModule, MatProgressBarModule, MatTooltipModule],
  template: `
    <h1>Nabídky</h1>

    <nav mat-tab-nav-bar [tabPanel]="panel">
      @for (tab of tabs; track tab.view) {
        <a mat-tab-link [routerLink]="[]" [queryParams]="{ view: tab.param }" [active]="current() === tab.view">
          {{ tab.label }}
          @if (list(); as l) { <span class="count">{{ l.counts[tab.countKey] }}</span> }
        </a>
      }
    </nav>

    <mat-tab-nav-panel #panel>
      @if (loading()) { <mat-progress-bar mode="indeterminate" /> }
      @if (failed()) { <p class="error">Nabídky se nepodařilo načíst. Zkuste prosím stránku obnovit.</p> }

      @if (list(); as l) {
        <div class="items">
          @for (item of l.items; track item.id) {
            <mat-card appearance="outlined" class="item">
              <div class="score {{ tone(item) }}" [matTooltip]="scoreHint(item)">{{ item.score ?? '–' }}</div>
              <div class="body">
                <a class="title" [routerLink]="['/matches', item.id]">{{ item.title }}</a>
                <div class="meta">{{ item.company }} · {{ item.location }} · nalezeno {{ item.foundAt | date: 'd. M.' }}</div>
                @if (item.summary) {
                  <p class="summary">{{ item.summary }}</p>
                } @else if (item.triageReason) {
                  <p class="summary muted">{{ item.triageReason }}</p>
                } @else if (item.stage === 'Shortlisted') {
                  <p class="summary muted">Čeká na ohodnocení (předběžná relevance {{ item.triageScore }}/5).</p>
                }
              </div>
              <div class="side">
                @if (item.status !== 'New') { <span class="status">{{ statusLabel(item.status) }}</span> }
                @if (item.hasCoverLetter) { <mat-icon matTooltip="Průvodní dopis je připraven">mail</mat-icon> }
                <a mat-icon-button [href]="item.url" target="_blank" rel="noopener" matTooltip="Otevřít inzerát">
                  <mat-icon>open_in_new</mat-icon>
                </a>
              </div>
            </mat-card>
          } @empty {
            <p class="empty">{{ emptyText() }}</p>
          }
        </div>
      }
    </mat-tab-nav-panel>
  `,
  styles: `
    .count { margin-left: 6px; padding: 0 8px; border-radius: 10px; background: var(--mat-sys-surface-container-high); font-size: 0.8em; }
    .items { display: flex; flex-direction: column; gap: 12px; padding-top: 16px; }
    .item { display: flex; flex-direction: row; align-items: flex-start; gap: 16px; padding: 16px; }
    .body { flex: 1; min-width: 0; }
    .title { font-weight: 600; font-size: 1.05rem; text-decoration: none; color: inherit; }
    .title:hover { text-decoration: underline; }
    .meta { opacity: 0.7; font-size: 0.9em; margin-top: 2px; }
    .summary { margin: 8px 0 0; }
    .side { display: flex; align-items: center; gap: 4px; }
    .status { font-size: 0.8em; padding: 2px 8px; border-radius: 8px; background: var(--mat-sys-secondary-container); }
    .empty { padding: 32px 0; text-align: center; opacity: 0.7; }
    @media (max-width: 600px) { .item { flex-wrap: wrap; } .side { width: 100%; justify-content: flex-end; } }
  `,
})
export class MatchesPage {
  private readonly matches = inject(MatchesService);

  /** ?view=… from the URL (component input binding) – tabs are links, so Back and bookmarks work. */
  readonly view = input<string>();

  protected readonly tabs = VIEWS;
  protected readonly current = computed(() => parseView(this.view()));
  protected readonly emptyText = computed(() => VIEWS.find(v => v.view === this.current())!.empty);
  protected readonly list = signal<MatchList | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);

  protected readonly statusLabel = statusLabel;

  constructor() {
    // Reload whenever the tab in the URL changes
    effect(() => {
      const view = this.current();
      untracked(() => this.load(view));
    });
  }

  protected tone(item: MatchListItem) {
    return scoreTone(item.score);
  }

  protected scoreHint(item: MatchListItem): string {
    return item.score === null ? 'Zatím neohodnoceno' : `Shoda s profilem ${item.score}/10`;
  }

  private async load(view: MatchView): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.list.set(await this.matches.list(view));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
