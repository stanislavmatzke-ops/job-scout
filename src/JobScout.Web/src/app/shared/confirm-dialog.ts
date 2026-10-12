import { Component, Injectable, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom } from 'rxjs';
import { t } from '../core/i18n';

export interface ConfirmData {
  title: string;
  message: string;
  confirm: string;
  cancel?: string;
}

/** A small "are you sure?" dialog – the data comes in through MAT_DIALOG_DATA, the answer leaves through mat-dialog-close. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">{{ data.cancel ?? t().common.cancel }}</button>
      <button mat-flat-button [mat-dialog-close]="true" cdkFocusInitial>{{ data.confirm }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
  protected readonly t = t;
}

/** `if (await confirm.ask({...}))` – the dialog as one awaitable call. Closing it any other way (Esc, click outside) = no. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(MatDialog);

  async ask(data: ConfirmData): Promise<boolean> {
    const ref = this.dialog.open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data, width: '440px' });
    return (await firstValueFrom(ref.afterClosed())) === true;
  }
}
