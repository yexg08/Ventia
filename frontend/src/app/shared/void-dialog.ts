import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { getErrorMessage, silentErrors } from '../core/http/api';

export interface VoidDialogData {
  title: string;
  /** que pasa al anular (se muestra debajo del motivo) */
  hint: string;
  /** endpoint que recibe { reason } */
  url: string;
  confirmText: string;
}

/** pide el motivo y anula el documento (cierra con lo que devuelva el servidor) */
@Component({
  selector: 'app-void-dialog',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="confirm()" class="card w-[min(28rem,calc(100vw-2rem))] p-6 !shadow-2xl" aria-labelledby="void-title">
      <h2 id="void-title" class="text-lg font-bold">{{ data.title }}</h2>
      <label for="void-reason" class="field-label mt-4">Motivo de la anulación</label>
      <input id="void-reason" class="field-input" maxlength="200" [(ngModel)]="reason" name="reason" required />
      @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
      <p class="mt-2 text-xs text-faint">{{ data.hint }}</p>
      <div class="mt-6 flex justify-end gap-2">
        <button type="button" class="btn-secondary" (click)="ref.close()" [disabled]="saving()">Cancelar</button>
        <button type="submit" class="btn-danger" [disabled]="saving() || !reason.trim()">{{ saving() ? 'Anulando…' : data.confirmText }}</button>
      </div>
    </form>
  `,
})
export class VoidDialog<T> {
  private readonly http = inject(HttpClient);
  protected readonly data = inject<VoidDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<T>>(DialogRef);

  protected reason = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected confirm(): void {
    if (!this.reason.trim()) return;
    this.saving.set(true);
    this.error.set(null);
    this.http.post<T>(this.data.url, { reason: this.reason.trim() }, { context: silentErrors() }).subscribe({
      next: (result) => this.ref.close(result),
      error: (e: unknown) => {
        this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}
