import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { documentError, isPerson } from '../../../shared/documents';
import { Customer, CustomerUpsert, DOCUMENT_TYPES, DocumentType } from '../business.models';
import { documentValidator, optional } from './party-forms';

type Field = 'documentNumber' | 'name' | 'phone' | 'email' | 'address' | 'notes' | 'dataConsent';

/** crear o editar un cliente (devuelve el cliente guardado al cerrar o nada si se cancela) */
@Component({
  selector: 'app-customer-dialog',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form
      [formGroup]="form"
      (ngSubmit)="save()"
      novalidate
      class="card flex max-h-[calc(100dvh-2rem)] w-[min(34rem,calc(100vw-2rem))] flex-col shadow-2xl"
      aria-labelledby="customer-dialog-title"
    >
      <div class="border-b border-line px-6 py-4">
        <h2 id="customer-dialog-title" class="text-lg font-bold">{{ customer ? 'Editar cliente' : 'Nuevo cliente' }}</h2>
      </div>

      <div class="space-y-5 overflow-y-auto px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }

        <div class="grid gap-5 sm:grid-cols-[13rem_1fr]">
          <div>
            <label for="customer-doc-type" class="field-label">Tipo de documento</label>
            <select id="customer-doc-type" formControlName="documentType" class="field-input">
              @for (type of documentTypes; track type.value) {
                <option [value]="type.value">{{ type.label }}</option>
              }
            </select>
          </div>
          <div>
            <label for="customer-doc" class="field-label">Número</label>
            <input id="customer-doc" formControlName="documentNumber" autocomplete="off" class="field-input" [attr.aria-invalid]="invalid('documentNumber')" />
            @if (invalid('documentNumber')) {
              <p class="field-error">{{ serverErrors()['documentNumber'] ?? documentMessage() }}</p>
            }
          </div>
        </div>

        <div>
          <label for="customer-name" class="field-label">{{ person() ? 'Nombre completo' : 'Razón social' }}</label>
          <input id="customer-name" formControlName="name" class="field-input" [attr.aria-invalid]="invalid('name')" />
          @if (invalid('name')) {
            <p class="field-error">{{ serverErrors()['name'] ?? 'Escribe el nombre (máximo 100 caracteres).' }}</p>
          }
        </div>

        <div class="grid gap-5 sm:grid-cols-2">
          <div>
            <label for="customer-phone" class="field-label">Celular <span class="font-normal text-faint">(opcional)</span></label>
            <input id="customer-phone" type="tel" formControlName="phone" class="field-input" [attr.aria-invalid]="invalid('phone')" />
            @if (invalid('phone')) {
              <p class="field-error">{{ serverErrors()['phone'] ?? 'De 7 a 15 dígitos.' }}</p>
            }
          </div>
          <div>
            <label for="customer-email" class="field-label">Correo <span class="font-normal text-faint">(opcional)</span></label>
            <input id="customer-email" type="email" formControlName="email" class="field-input" [attr.aria-invalid]="invalid('email')" />
            @if (invalid('email')) {
              <p class="field-error">{{ serverErrors()['email'] ?? 'Escribe un correo válido.' }}</p>
            }
          </div>
        </div>

        <div>
          <label for="customer-address" class="field-label">Dirección <span class="font-normal text-faint">(opcional, para domicilios)</span></label>
          <input id="customer-address" formControlName="address" class="field-input" [attr.aria-invalid]="invalid('address')" />
        </div>

        <div>
          <label for="customer-notes" class="field-label">Notas internas <span class="font-normal text-faint">(el cliente no las ve)</span></label>
          <textarea id="customer-notes" formControlName="notes" rows="2" class="field-input resize-none" [attr.aria-invalid]="invalid('notes')"></textarea>
        </div>

        @if (person()) {
          @if (customer?.dataConsentAt) {
            <p class="rounded-xl border border-line bg-surface-2 px-3.5 py-3 text-xs text-muted">
              Autorizó el tratamiento de sus datos el {{ consentDate() }}.
            </p>
          } @else {
            <label class="flex cursor-pointer items-start gap-3 rounded-xl border p-3.5" [class]="invalid('dataConsent') ? 'border-danger' : 'border-line'">
              <input type="checkbox" formControlName="dataConsent" class="mt-0.5 size-4 accent-accent" />
              <span>
                <span class="block text-sm font-medium">El cliente autorizó el tratamiento de sus datos</span>
                <span class="block text-xs text-muted">Pídele permiso antes de guardar sus datos (Ley 1581 de 2012). Puede pedir que los borres cuando quiera.</span>
              </span>
            </label>
            @if (invalid('dataConsent')) {
              <p class="field-error -mt-3">{{ serverErrors()['dataConsent'] ?? 'Sin su autorización no se pueden guardar sus datos.' }}</p>
            }
          }
        }
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="submit" class="btn-primary" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar' }}</button>
      </div>
    </form>
  `,
})
export class CustomerDialog {
  private readonly http = inject(HttpClient);
  protected readonly customer = inject<Customer | null>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Customer>>(DialogRef);
  protected readonly documentTypes = DOCUMENT_TYPES;

  private readonly c = this.customer;
  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      documentType: [this.c?.documentType ?? ('CC' as DocumentType)],
      documentNumber: [this.c?.documentNumber ?? '', [Validators.required]],
      name: [this.c?.name ?? '', [Validators.required, Validators.maxLength(100)]],
      phone: [this.c?.phone ?? '', [Validators.pattern(/^\+?[0-9][0-9 -]{5,18}[0-9]$/)]],
      email: [this.c?.email ?? '', [Validators.email, Validators.maxLength(256)]],
      address: [this.c?.address ?? '', [Validators.maxLength(120)]],
      notes: [this.c?.notes ?? '', [Validators.maxLength(500)]],
      dataConsent: [false],
    },
    { validators: [documentValidator] },
  );

  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly person = computed(() => isPerson(this.values().documentType ?? 'CC'));
  protected readonly documentMessage = computed(() => documentError(this.values().documentType ?? 'CC'));
  protected readonly consentDate = computed(() =>
    this.c?.dataConsentAt ? new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium' }).format(new Date(this.c.dataConsentAt)) : '',
  );

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  protected invalid(control: Field): boolean {
    const c = this.form.controls[control];
    const documentBad = control === 'documentNumber' && this.form.hasError('document') && c.touched;
    const consentMissing = control === 'dataConsent' && this.submitted() && this.needsConsent();
    return (c.invalid && c.touched) || documentBad || consentMissing || !!this.serverErrors()[control];
  }

  private readonly submitted = signal(false);
  private needsConsent(): boolean {
    return this.person() && !this.c?.dataConsentAt && !this.form.controls.dataConsent.value;
  }

  protected save(): void {
    this.submitted.set(true);
    if (this.form.invalid || this.needsConsent()) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const body: CustomerUpsert = {
      documentType: raw.documentType,
      documentNumber: raw.documentNumber.trim(),
      name: raw.name.trim(),
      phone: optional(raw.phone),
      email: optional(raw.email),
      address: optional(raw.address),
      notes: optional(raw.notes),
      dataConsent: raw.dataConsent,
    };
    const request = this.c
      ? this.http.put<Customer>(`${API_BASE}/customers/${this.c.id}`, body, { context: silentErrors() })
      : this.http.post<Customer>(`${API_BASE}/customers`, body, { context: silentErrors() });

    this.saving.set(true);
    this.error.set(null);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}
