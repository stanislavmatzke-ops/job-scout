import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { DEFAULT_SUSPEND_REASON, MAX_SUSPEND_REASON } from '../../core/admin.service';

export interface SuspendData {
  displayName: string;
}

/** Asks for the reason – the user sees it word for word in the banner, so it is prefilled with a polite default. */
@Component({
  selector: 'app-suspend-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Pozastavit účet – {{ data.displayName }}</h2>
    <mat-dialog-content>
      <p>Uživatel se dál přihlásí a uvidí své nabídky, ale ranní vyhledávání a funkce AI se pro něj vypnou.
        Důvod uvidí v aplikaci přesně takto:</p>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Důvod pozastavení</mat-label>
        <textarea matInput rows="3" [maxlength]="max" [ngModel]="reason()" (ngModelChange)="reason.set($event)"></textarea>
        <mat-hint align="end">{{ reason().length }} / {{ max }}</mat-hint>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">Zrušit</button>
      <button mat-flat-button [disabled]="!reason().trim()" [mat-dialog-close]="reason().trim()">Pozastavit</button>
    </mat-dialog-actions>
  `,
  styles: `.full { width: 100%; }`,
})
export class SuspendDialog {
  protected readonly data = inject<SuspendData>(MAT_DIALOG_DATA);
  protected readonly max = MAX_SUSPEND_REASON;
  protected readonly reason = signal(DEFAULT_SUSPEND_REASON);
}
