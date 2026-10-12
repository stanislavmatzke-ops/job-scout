import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MAX_SUSPEND_REASON } from '../../core/admin.service';
import { t } from '../../core/i18n';

export interface SuspendData {
  displayName: string;
}

/** Asks for the reason – the user sees it word for word in the banner, so it is prefilled with a polite default. */
@Component({
  selector: 'app-suspend-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>{{ t().admin.suspendTitle(data.displayName) }}</h2>
    <mat-dialog-content>
      <p>{{ t().admin.suspendHelp }}</p>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ t().admin.suspendReason }}</mat-label>
        <textarea matInput rows="3" [maxlength]="max" [ngModel]="reason()" (ngModelChange)="reason.set($event)"></textarea>
        <mat-hint align="end">{{ reason().length }} / {{ max }}</mat-hint>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">{{ t().common.cancel }}</button>
      <button mat-flat-button [disabled]="!reason().trim()" [mat-dialog-close]="reason().trim()">{{ t().admin.suspend }}</button>
    </mat-dialog-actions>
  `,
  styles: `.full { width: 100%; }`,
})
export class SuspendDialog {
  protected readonly data = inject<SuspendData>(MAT_DIALOG_DATA);
  protected readonly max = MAX_SUSPEND_REASON;
  protected readonly t = t;
  /** Prefilled in the admin's language – the user sees it word for word. */
  protected readonly reason = signal(t().admin.defaultReason);
}
