import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Clipboard } from '@angular/cdk/clipboard';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth.service';
import { MatchesService } from '../../core/matches.service';
import { MatchDetail, MatchStatus } from '../../core/matches.models';
import {
  STATUS_OPTIONS, aiErrorMessage, employmentLabel, problemMessage, remoteLabel, scoreTone, statusLabel,
} from '../../core/match-format';
import { ConfirmService } from '../../shared/confirm-dialog';

/** What the AI is doing right now – one thing at a time, so a double click never pays twice. */
type AiWork = 'evaluate' | 'letter';

@Component({
  selector: 'app-match-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, RouterLink, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatExpansionModule, MatProgressBarModule,
  ],
  template: `
    <a mat-button routerLink="/matches"><mat-icon>arrow_back</mat-icon> Zpět na nabídky</a>

    @if (loadError(); as message) { <p class="error">{{ message }}</p> }

    @if (detail(); as d) {
      <header class="head">
        <div class="score {{ tone(d.score) }}">{{ d.score ?? '–' }}</div>
        <div class="head-text">
          <h1>{{ d.title }}</h1>
          <div class="meta">{{ d.company }} · {{ d.location }} · {{ d.source }} · nalezeno {{ d.foundAt | date: 'd. M. y' }}</div>
        </div>
        <a mat-stroked-button [href]="d.url" target="_blank" rel="noopener"><mat-icon>open_in_new</mat-icon> Otevřít inzerát</a>
      </header>

      @if (!aiEnabled()) {
        <p class="ai-off">Funkce AI (hodnocení a psaní dopisů) jsou u pozastaveného účtu vypnuté.</p>
      }

      <div class="grid">
        <section class="column">
          <!-- ===== Evaluation ===== -->
          <mat-card appearance="outlined">
            <mat-card-header>
              <mat-card-title>Hodnocení</mat-card-title>
            </mat-card-header>
            @if (working() === 'evaluate') {
              <mat-progress-bar mode="indeterminate" />
            }
            <mat-card-content>
              @if (working() === 'evaluate') {
                <p class="muted">Načítám inzerát a hodnotím ho podle vašeho profilu – obvykle do 20 sekund…</p>
              } @else if (d.evaluation; as e) {
                @if (d.evaluationOutdated) {
                  <p class="outdated">Ohodnoceno podle starší verze vašeho profilu – od té doby jste změnili CV nebo poznámky.</p>
                }
                <p class="summary">{{ e.summary }}</p>
                <dl class="facts">
                  <dt>Mzda</dt><dd>{{ e.salary || 'Neuvedena' }}</dd>
                  <dt>Práce z domova</dt><dd>{{ remoteLabel(e.remoteWork) }}</dd>
                  <dt>Forma spolupráce</dt><dd>{{ employmentLabel(e.employmentType) }}</dd>
                </dl>
                @if (e.reasons.length) {
                  <h3>Proč nabídka odpovídá</h3>
                  <ul class="reasons">@for (r of e.reasons; track $index) { <li>{{ r }}</li> }</ul>
                }
                @if (e.risks.length) {
                  <h3>Rizika</h3>
                  <ul class="risks">@for (r of e.risks; track $index) { <li>{{ r }}</li> }</ul>
                }
                @if (e.questions.length) {
                  <h3>Na co se zeptat</h3>
                  <ul class="questions">@for (q of e.questions; track $index) { <li>{{ q }}</li> }</ul>
                }
              } @else if (d.stage === 'TriagedOut') {
                <p>Nabídku vyřadilo automatické třídění: <em>{{ d.triageReason }}</em></p>
                <p class="muted">Pokud si myslíte, že třídění chybovalo, nechte nabídku přesto ohodnotit.</p>
              } @else {
                <p class="muted">Nabídka zatím nebyla ohodnocena@if (d.triageScore) { (předběžná relevance {{ d.triageScore }}/5)}.</p>
              }
            </mat-card-content>
            @if (evaluateLabel(d); as label) {
              <mat-card-actions>
                <button mat-stroked-button (click)="evaluate(d)" [disabled]="!aiEnabled() || working() !== null">
                  <mat-icon>auto_awesome</mat-icon> {{ label }}
                </button>
              </mat-card-actions>
            }
          </mat-card>

          <!-- ===== Cover letter ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>Průvodní dopis</mat-card-title></mat-card-header>
            @if (working() === 'letter') {
              <mat-progress-bar mode="indeterminate" />
            }
            <mat-card-content>
              @if (working() === 'letter') {
                <p class="muted">Píšu návrh dopisu podle vašeho profilu a inzerátu…</p>
              } @else if (editingLetter()) {
                <mat-form-field appearance="outline" class="full">
                  <mat-label>Text dopisu</mat-label>
                  <textarea matInput rows="16" name="letter" maxlength="10000" [(ngModel)]="letterDraft"></textarea>
                </mat-form-field>
              } @else if (d.coverLetter) {
                @if (d.evaluationOutdated) {
                  <p class="outdated">Dopis vznikl podle starší verze profilu. Zkontrolujte ho, případně nechte napsat nový návrh.</p>
                }
                <pre class="letter">{{ d.coverLetter }}</pre>
              } @else if (d.stage === 'Evaluated') {
                <p class="muted">K této nabídce zatím není připraven žádný dopis.</p>
              } @else {
                <p class="muted">Dopis lze napsat, jakmile bude nabídka ohodnocena – vychází z hodnocení.</p>
              }
            </mat-card-content>
            <mat-card-actions class="actions">
              @if (editingLetter()) {
                <button mat-flat-button (click)="saveLetter(d)" [disabled]="saving() || !letterDraft.trim()">Uložit dopis</button>
                <button mat-button (click)="editingLetter.set(false)">Zrušit</button>
              } @else {
                @if (d.coverLetter) {
                  <button mat-stroked-button (click)="copyLetter(d.coverLetter)"><mat-icon>content_copy</mat-icon> Kopírovat</button>
                  <button mat-button (click)="editLetter(d)" [disabled]="working() !== null"><mat-icon>edit</mat-icon> Upravit</button>
                }
                @if (d.stage === 'Evaluated') {
                  <button mat-button (click)="writeLetter(d)" [disabled]="!aiEnabled() || working() !== null">
                    <mat-icon>auto_awesome</mat-icon> {{ d.coverLetter ? 'Napsat nový návrh' : 'Napsat dopis' }}
                  </button>
                }
              }
            </mat-card-actions>
          </mat-card>

          @if (d.adText) {
            <mat-expansion-panel>
              <mat-expansion-panel-header><mat-panel-title>Text inzerátu</mat-panel-title></mat-expansion-panel-header>
              @if (d.adTextIncomplete) {
                <p class="muted">Text inzerátu se nepodařilo načíst celý – podrobnosti najdete na původní stránce.</p>
              }
              <pre class="ad">{{ d.adText }}</pre>
            </mat-expansion-panel>
          }
        </section>

        <aside class="column">
          <!-- ===== Status ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>Stav</mat-card-title></mat-card-header>
            <mat-card-content>
              <mat-form-field appearance="outline" class="full">
                <mat-label>Stav nabídky</mat-label>
                <mat-select [(ngModel)]="newStatus" name="status">
                  @for (s of statuses; track s.value) { <mat-option [value]="s.value">{{ s.label }}</mat-option> }
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" class="full">
                <mat-label>Poznámka ke změně (nepovinné)</mat-label>
                <input matInput name="statusNote" maxlength="2000" [(ngModel)]="statusNote" placeholder="Např. odesláno přes jobs.cz" />
              </mat-form-field>
              <button mat-flat-button (click)="saveStatus(d)" [disabled]="saving() || (newStatus === d.status && !statusNote.trim())">
                Uložit stav
              </button>
            </mat-card-content>
          </mat-card>

          <!-- ===== Notes ===== -->
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>Poznámky</mat-card-title></mat-card-header>
            <mat-card-content>
              <mat-form-field appearance="outline" class="full">
                <textarea matInput rows="5" name="notes" maxlength="4000" [(ngModel)]="notes"
                          placeholder="Kontakty, termíny, dojmy z pohovoru…"></textarea>
              </mat-form-field>
              <button mat-stroked-button (click)="saveNotes(d)" [disabled]="saving() || notes === (d.notes ?? '')">Uložit poznámky</button>
            </mat-card-content>
          </mat-card>

          <!-- ===== History ===== -->
          @if (d.history.length) {
            <mat-card appearance="outlined">
              <mat-card-header><mat-card-title>Historie</mat-card-title></mat-card-header>
              <mat-card-content>
                <ol class="history">
                  @for (h of d.history; track $index) {
                    <li>
                      <span class="when">{{ h.at | date: 'd. M. y H:mm' }}</span>
                      {{ statusLabel(h.from) }} → <strong>{{ statusLabel(h.to) }}</strong>
                      @if (h.note) { <div class="note">{{ h.note }}</div> }
                    </li>
                  }
                </ol>
              </mat-card-content>
            </mat-card>
          }
        </aside>
      </div>
    }
  `,
  styles: `
    .head { display: flex; align-items: center; gap: 16px; margin: 8px 0 24px; flex-wrap: wrap; }
    .head-text { flex: 1; min-width: 240px; }
    h1 { margin: 0; font-size: 1.6rem; }
    .meta { opacity: 0.7; margin-top: 4px; }
    .ai-off { padding: 8px 12px; border-radius: 8px; background: var(--mat-sys-surface-container-high); }
    .outdated { padding: 6px 10px; border-radius: 8px; border: 1px dashed var(--mat-sys-outline); font-size: 0.9em; }
    .grid { display: grid; grid-template-columns: 2fr 1fr; gap: 16px; align-items: start; }
    .column { display: flex; flex-direction: column; gap: 16px; min-width: 0; }
    .summary { font-size: 1.05rem; }
    .facts { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; }
    .facts dt { opacity: 0.7; }
    .facts dd { margin: 0; }
    h3 { margin: 16px 0 4px; font-size: 1rem; }
    .reasons li::marker { content: '✓  '; color: #2e7d32; }
    .risks li::marker { content: '!  '; color: #c62828; }
    .letter, .ad { white-space: pre-wrap; font-family: inherit; margin: 0; }
    .actions { flex-wrap: wrap; gap: 4px; }
    .full { width: 100%; }
    .history { padding-left: 18px; margin: 0; }
    .history li { margin-bottom: 8px; }
    .when { opacity: 0.7; font-size: 0.85em; margin-right: 6px; }
    .note { font-size: 0.9em; opacity: 0.85; }
    @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class MatchDetailPage {
  private readonly matches = inject(MatchesService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly clipboard = inject(Clipboard);
  private readonly snackBar = inject(MatSnackBar);
  private readonly confirm = inject(ConfirmService);

  /** :id from the route (component input binding). */
  readonly id = input.required<string>();

  protected readonly detail = signal<MatchDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly working = signal<AiWork | null>(null);
  protected readonly editingLetter = signal(false);

  /** Only Approved users may spend AI tokens (ADR-0017) – the API enforces it, the UI just doesn't offer it. */
  protected readonly aiEnabled = computed(() => this.auth.me()?.access === 'Approved');

  protected readonly statuses = STATUS_OPTIONS;
  protected readonly statusLabel = statusLabel;
  protected readonly remoteLabel = remoteLabel;
  protected readonly employmentLabel = employmentLabel;
  protected readonly tone = scoreTone;

  // Form fields – plain fields are enough with [(ngModel)]; reset from every loaded detail
  protected newStatus: MatchStatus = 'New';
  protected statusNote = '';
  protected notes = '';
  protected letterDraft = '';

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  // ---------- AI on demand ----------

  /**
   * A fresh evaluation is not offered again – the same profile and ad would give the same result and cost money.
   * Only: not evaluated yet, triaged out ("anyway"), or evaluated with an older profile version.
   */
  protected evaluateLabel(d: MatchDetail): string | null {
    if (!d.evaluation) return d.stage === 'TriagedOut' ? 'Přesto ohodnotit' : 'Ohodnotit';
    return d.evaluationOutdated ? 'Přehodnotit podle aktuálního profilu' : null;
  }

  protected async evaluate(d: MatchDetail): Promise<void> {
    await this.runAi('evaluate', d.id, async () => {
      this.show(await this.matches.evaluate(d.id));
      this.toast('Nabídka byla ohodnocena.');
    });
  }

  protected async writeLetter(d: MatchDetail): Promise<void> {
    const replace = d.coverLetter !== null;
    if (replace && !(await this.confirm.ask({
      title: 'Napsat nový návrh dopisu?',
      message: 'Stávající dopis včetně vašich úprav bude nahrazen novým návrhem.',
      confirm: 'Napsat nový návrh',
    }))) {
      return;
    }

    await this.runAi('letter', d.id, async () => {
      this.show(await this.matches.writeLetter(d.id, replace));
      this.toast('Návrh dopisu je připraven.');
    });
  }

  // ---------- the user's own changes ----------

  protected editLetter(d: MatchDetail): void {
    this.letterDraft = d.coverLetter ?? '';
    this.editingLetter.set(true);
  }

  protected async saveLetter(d: MatchDetail): Promise<void> {
    await this.save(async () => {
      await this.matches.updateLetter(d.id, this.letterDraft.trim());
      await this.load(d.id);
      this.toast('Dopis byl uložen.');
    });
  }

  protected async saveStatus(d: MatchDetail): Promise<void> {
    await this.save(async () => {
      await this.matches.changeStatus(d.id, this.newStatus, this.statusNote.trim() || null);
      if (this.newStatus === 'Hidden') {
        this.toast('Nabídka byla skryta.');
        await this.router.navigate(['/matches']);
        return;
      }
      await this.load(d.id);
      this.toast('Stav byl uložen.');
    });
  }

  protected async saveNotes(d: MatchDetail): Promise<void> {
    await this.save(async () => {
      await this.matches.updateNotes(d.id, this.notes.trim() || null);
      await this.load(d.id);
      this.toast('Poznámky byly uloženy.');
    });
  }

  protected copyLetter(text: string): void {
    this.toast(this.clipboard.copy(text) ? 'Dopis byl zkopírován do schránky.' : 'Dopis se nepodařilo zkopírovat.');
  }

  // ---------- plumbing ----------

  private async load(id: string): Promise<void> {
    try {
      this.show(await this.matches.detail(id));
      this.loadError.set(null);
    } catch (error) {
      this.loadError.set(error instanceof HttpErrorResponse && error.status === 404
        ? 'Nabídka nebyla nalezena.'
        : 'Nabídku se nepodařilo načíst. Zkuste prosím stránku obnovit.');
    }
  }

  /** Every fresh detail (load, evaluation, new letter) resets the form fields. */
  private show(d: MatchDetail): void {
    this.newStatus = d.status;
    this.statusNote = '';
    this.notes = d.notes ?? '';
    this.editingLetter.set(false);
    this.detail.set(d);                                     // set last – this redraws the page
  }

  private async runAi(work: AiWork, id: string, action: () => Promise<void>): Promise<void> {
    if (this.working()) return;                             // one AI call at a time
    this.working.set(work);
    try {
      await action();
    } catch (error) {
      this.toast(aiErrorMessage(error), 6000);
      // 410 = the ad was removed from the portal – the API moved the job to "Vyřazené", show it
      if (error instanceof HttpErrorResponse && error.status === 410) await this.load(id);
    } finally {
      this.working.set(null);
    }
  }

  private async save(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    try {
      await action();
    } catch (error) {
      this.toast(problemMessage(error, 'Změnu se nepodařilo uložit. Zkuste to prosím znovu.'), 6000);
    } finally {
      this.saving.set(false);
    }
  }

  private toast(message: string, duration = 3000): void {
    this.snackBar.open(message, undefined, { duration });
  }
}
