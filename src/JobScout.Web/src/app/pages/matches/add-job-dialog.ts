import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatchesService } from '../../core/matches.service';
import { AddJobResult, MatchStatus } from '../../core/matches.models';
import { STATUS_OPTIONS, problemMessage } from '../../core/match-format';
import { LimitsService, blockedMessage, limitMessage, usageHint } from '../../core/limits.service';
import { t } from '../../core/i18n';

type Mode = 'text' | 'file' | 'url';

/** What the dialog closes with: the API's answer + whether the evaluation was asked for (not evaluated then = it failed). */
export interface AddJobOutcome extends AddJobResult { evaluateRequested: boolean; }

/** The same 5 MB as the server (AddJobManually.MaxFileBytes) – said here before the upload, not after it. */
const MAX_FILE_BYTES = 5 * 1024 * 1024;

/**
 * "Přidat nabídku" (ADR-0013): a job ad from elsewhere – pasted text, a file or a link, the status it already has,
 * and whether to have it evaluated now. Closes with the result; the list page then opens the job.
 */
@Component({
  selector: 'app-add-job-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ t().addJob.title }}</h2>
    <mat-dialog-content>
      <p class="intro">{{ t().addJob.intro }}</p>

      <mat-button-toggle-group [value]="mode()" (change)="mode.set($event.value)" [disabled]="working()" class="modes"
                               [attr.aria-label]="t().addJob.title">
        <mat-button-toggle value="text"><mat-icon>notes</mat-icon> {{ t().addJob.modeText }}</mat-button-toggle>
        <mat-button-toggle value="file"><mat-icon>attach_file</mat-icon> {{ t().addJob.modeFile }}</mat-button-toggle>
        <mat-button-toggle value="url"><mat-icon>link</mat-icon> {{ t().addJob.modeUrl }}</mat-button-toggle>
      </mat-button-toggle-group>

      @switch (mode()) {
        @case ('text') {
          <mat-form-field appearance="outline" class="full">
            <mat-label>{{ t().addJob.text }}</mat-label>
            <textarea matInput rows="8" name="text" maxlength="50000" [disabled]="working()"
                      [ngModel]="text()" (ngModelChange)="text.set($event)" [placeholder]="t().addJob.textPlaceholder"></textarea>
          </mat-form-field>
        }
        @case ('file') {
          <div class="file">
            <input #picker type="file" hidden accept=".pdf,.docx,.txt,.md,.eml" (change)="onFile(picker)" />
            <button mat-stroked-button (click)="picker.click()" [disabled]="working()">
              <mat-icon>upload_file</mat-icon> {{ t().addJob.chooseFile }}
            </button>
            <span class="file-name">{{ file()?.name ?? t().addJob.noFile }}</span>
          </div>
          <p class="hint">{{ t().addJob.fileFormats }}</p>
        }
        @case ('url') {
          <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
            <mat-label>{{ t().addJob.url }}</mat-label>
            <input matInput type="url" name="url" maxlength="2000" placeholder="https://…" [disabled]="working()"
                   [ngModel]="url()" (ngModelChange)="url.set($event)" />
            <mat-hint>{{ t().addJob.urlHint }}</mat-hint>
          </mat-form-field>
        }
      }

      <div class="options">
        <mat-form-field appearance="outline" class="status" subscriptSizing="dynamic">
          <mat-label>{{ t().addJob.status }}</mat-label>
          <mat-select name="status" [disabled]="working()" [ngModel]="status()" (ngModelChange)="status.set($event)">
            @for (s of statuses; track s.value) { <mat-option [value]="s.value">{{ s.label }}</mat-option> }
          </mat-select>
          <mat-hint>{{ t().addJob.statusHint }}</mat-hint>
        </mat-form-field>
        <div>
          <mat-checkbox [disabled]="working()" [checked]="evaluate()" (change)="evaluate.set($event.checked)">
            {{ t().addJob.evaluate }}
          </mat-checkbox>
          @if (evaluate() && evaluationHint(); as hint) { <p class="hint">{{ hint }}</p> }
        </div>
      </div>

      @if (importHint(); as hint) { <p class="hint">{{ hint }}</p> }
      @if (blocked(); as message) { <p class="error" role="status">{{ message }}</p> }

      @if (working()) {
        <mat-progress-bar mode="indeterminate" />
        <p class="muted" role="status">{{ evaluate() ? t().addJob.working : t().addJob.workingNoEval }}</p>
      }
      @if (error(); as message) { <p class="error" role="alert">{{ message }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="working()">{{ t().common.cancel }}</button>
      <button mat-flat-button (click)="submit()" [disabled]="!ready() || working() || !!blocked()">
        <mat-icon>add</mat-icon> {{ t().addJob.submit }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .intro { margin-top: 0; }
    .modes { margin-bottom: 16px; }
    .full { width: 100%; }
    .file { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .file-name { color: var(--js-muted); overflow-wrap: anywhere; }
    .options { display: flex; flex-wrap: wrap; gap: 8px 24px; align-items: flex-start; margin: 16px 0 8px; }
    .status { width: 260px; }
    .hint { color: var(--js-muted); font-size: 0.85em; margin: 4px 0 8px; }
    .muted { color: var(--js-muted); }
    .error { color: var(--mat-sys-error); }
  `,
})
export class AddJobDialog {
  private readonly matches = inject(MatchesService);
  private readonly limitsService = inject(LimitsService);
  private readonly dialogRef = inject<MatDialogRef<AddJobDialog, AddJobOutcome>>(MatDialogRef);

  protected readonly mode = signal<Mode>('text');
  protected readonly text = signal('');
  protected readonly url = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly status = signal<MatchStatus>('New');
  protected readonly evaluate = signal(true);
  protected readonly working = signal(false);
  protected readonly error = signal<string | null>(null);

  /** "Hidden" makes no sense for a job one is just adding. */
  protected readonly statuses = STATUS_OPTIONS.filter(s => s.value !== 'Hidden');
  protected readonly t = t;

  protected readonly ready = computed(() => {
    switch (this.mode()) {
      case 'text': return this.text().trim().length > 0;
      case 'file': return this.file() !== null;
      case 'url': return /^https?:\/\/\S+$/i.test(this.url().trim());
    }
  });

  // AI limits for clicks (ADR-0021) – the server decides, the dialog only says what is left
  protected readonly blocked = computed(() => blockedMessage(this.limitsService.limits()));
  protected readonly importHint = computed(() =>
    usageHint(this.limitsService.limits(), l => l.imports, t().limits.importsLeft));
  protected readonly evaluationHint = computed(() =>
    usageHint(this.limitsService.limits(), l => l.evaluations, t().limits.evaluationsLeft));

  constructor() {
    void this.limitsService.refresh();
  }

  protected onFile(input: HTMLInputElement): void {
    const file = input.files?.[0] ?? null;
    input.value = '';                                       // choosing the same file again fires (change) again
    this.error.set(file && file.size > MAX_FILE_BYTES ? t().addJob.tooBig : null);
    this.file.set(file && file.size <= MAX_FILE_BYTES ? file : null);
  }

  protected async submit(): Promise<void> {
    if (!this.ready() || this.working()) return;
    this.working.set(true);
    this.dialogRef.disableClose = true;                     // the AI is paid for already – don't lose the answer
    this.error.set(null);
    try {
      const mode = this.mode();
      const result = await this.matches.addManually({
        text: mode === 'text' ? this.text().trim() : undefined,
        url: mode === 'url' ? this.url().trim() : undefined,
        file: mode === 'file' ? this.file() ?? undefined : undefined,
        status: this.status(),
        evaluate: this.evaluate(),
      });
      this.dialogRef.close({ ...result, evaluateRequested: this.evaluate() });
    } catch (error) {
      this.error.set(addJobError(error));
    } finally {
      this.working.set(false);
      this.dialogRef.disableClose = false;
      void this.limitsService.refresh();
    }
  }
}

/** In Czech the API already says what happened; in English we say it by the status code (and the 422 reason). */
export function addJobError(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  const e = t().errors;
  switch (status) {
    case 429: return limitMessage(error);
    case 403: return e.aiUnavailable;
    case 409: return problemMessage(error, e.ai409);
    case 422: {
      const reason = (error as HttpErrorResponse).error?.reason;
      return problemMessage(error, reason === 'NotAJobAd' ? t().addJob.notAJobAd : t().addJob.pageUnavailable);
    }
    case 503: return problemMessage(error, e.ai503);
    default: return problemMessage(error, t().common.actionFailed);
  }
}
