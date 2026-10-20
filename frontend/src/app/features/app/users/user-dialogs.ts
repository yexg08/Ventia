import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroCheck, heroClipboardDocument, heroExclamationTriangle, heroKey } from '@ng-icons/heroicons/outline';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { formatDate } from '../../../shared/format';
import { TemporaryPassword } from '../business.models';

/** pide el nombre y el correo del cajero y crea su cuenta (cierra con la contrasena temporal) */
@Component({
  selector: 'app-new-cashier-dialog',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()" novalidate class="card w-[min(28rem,calc(100vw-2rem))] p-6 !shadow-2xl" aria-labelledby="cashier-title">
      <h2 id="cashier-title" class="text-lg font-bold">Nuevo cajero</h2>
      <p class="mt-1 text-sm text-muted">Podrá vender y ver el menú. No verá costos ni podrá cambiar la configuración del negocio.</p>

      @if (error()) {
        <p class="mt-4 rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
      }

      <div class="mt-5 space-y-5">
        <div>
          <label for="cashier-name" class="field-label">Nombre</label>
          <input id="cashier-name" formControlName="fullName" autocomplete="off" class="field-input" [attr.aria-invalid]="invalid('fullName')" />
          @if (invalid('fullName')) {
            <p class="field-error">{{ serverErrors()['fullName'] ?? 'Escribe su nombre.' }}</p>
          }
        </div>
        <div>
          <label for="cashier-email" class="field-label">Correo</label>
          <input id="cashier-email" type="email" formControlName="email" autocomplete="off" class="field-input" [attr.aria-invalid]="invalid('email')" />
          @if (invalid('email')) {
            <p class="field-error">{{ serverErrors()['email'] ?? 'Escribe un correo válido.' }}</p>
          }
        </div>
      </div>

      <div class="mt-6 flex justify-end gap-2">
        <button type="button" class="btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="submit" class="btn-primary" [disabled]="saving()">{{ saving() ? 'Creando…' : 'Crear cuenta' }}</button>
      </div>
    </form>
  `,
})
export class NewCashierDialog {
  private readonly http = inject(HttpClient);
  protected readonly ref = inject<DialogRef<{ name: string; result: TemporaryPassword }>>(DialogRef);
  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.maxLength(80)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
  });
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  protected invalid(control: 'fullName' | 'email'): boolean {
    const c = this.form.controls[control];
    return (c.invalid && c.touched) || !!this.serverErrors()[control];
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const body = { fullName: raw.fullName.trim(), email: raw.email.trim() };
    this.saving.set(true);
    this.error.set(null);
    this.http.post<TemporaryPassword>(`${API_BASE}/users`, body, { context: silentErrors() }).subscribe({
      next: (result) => this.ref.close({ name: body.fullName, result }),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}

export interface TemporaryPasswordData {
  name: string;
  business: string;
  result: TemporaryPassword;
}

/** muestra la contrasena temporal UNA sola vez con un mensaje listo para enviar por WhatsApp */
@Component({
  selector: 'app-temporary-password-dialog',
  imports: [NgIcon],
  providers: [provideIcons({ heroCheck, heroClipboardDocument, heroExclamationTriangle, heroKey })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card w-[min(30rem,calc(100vw-2rem))] p-6 !shadow-2xl" role="dialog" aria-labelledby="temp-title">
      <div class="flex size-11 items-center justify-center rounded-xl bg-surface-2 text-accent"><ng-icon name="heroKey" size="22" /></div>
      <h2 id="temp-title" class="mt-4 text-lg font-bold">Cuenta lista para {{ data.name }}</h2>
      <p class="mt-1 text-sm text-muted">Entra con <strong class="text-ink">{{ data.result.email }}</strong> y esta contraseña temporal:</p>

      <p class="mt-4 rounded-xl border border-line bg-surface-2 px-4 py-3 text-center font-mono text-lg font-bold tracking-wider select-all">
        {{ data.result.temporaryPassword }}
      </p>
      <p class="mt-3 flex items-start gap-2 text-xs text-muted">
        <ng-icon name="heroExclamationTriangle" size="15" class="mt-px shrink-0 text-accent" />
        Solo la verás ahora. Vence el {{ expires }} y al entrar le pedirá elegir su propia contraseña.
      </p>

      <div class="mt-6 grid gap-2 sm:grid-cols-2">
        <button type="button" class="btn-secondary" (click)="copy(data.result.temporaryPassword, 'password')">
          <ng-icon [name]="copied() === 'password' ? 'heroCheck' : 'heroClipboardDocument'" size="17" />
          {{ copied() === 'password' ? 'Copiada' : 'Copiar contraseña' }}
        </button>
        <button type="button" class="btn-primary" (click)="copy(message(), 'message')">
          <ng-icon [name]="copied() === 'message' ? 'heroCheck' : 'heroClipboardDocument'" size="17" />
          {{ copied() === 'message' ? 'Mensaje copiado' : 'Copiar mensaje' }}
        </button>
      </div>
      <button type="button" class="btn-ghost mt-2 w-full" (click)="ref.close()">Listo</button>
    </div>
  `,
})
export class TemporaryPasswordDialog {
  private readonly toasts = inject(ToastService);
  protected readonly data = inject<TemporaryPasswordData>(DIALOG_DATA);
  protected readonly ref = inject(DialogRef);
  protected readonly copied = signal<'password' | 'message' | null>(null);
  protected readonly expires = formatDate(this.data.result.expiresAt);

  protected readonly message = computed(
    () =>
      `Hola ${this.data.name.split(' ')[0]}, ya tienes tu cuenta de cajero en ${this.data.business} (Ventia).\n\n` +
      `Entra en: ${location.origin}/login\nCorreo: ${this.data.result.email}\nContraseña temporal: ${this.data.result.temporaryPassword}\n\n` +
      `Vence el ${this.expires}. Al entrar te pedirá elegir tu propia contraseña.`,
  );

  protected async copy(text: string, what: 'password' | 'message'): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(what);
      setTimeout(() => this.copied.set(null), 2000);
    } catch {
      this.toasts.error('No se pudo copiar. Selecciona el texto y cópialo a mano.');
    }
  }
}
