import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { documentError } from '../../../shared/documents';
import { DOCUMENT_TYPES, DocumentType, Supplier, SupplierUpsert } from '../business.models';
import { documentValidator, optional } from './party-forms';

type Field = 'documentNumber' | 'name' | 'contactName' | 'phone' | 'email';

/** crear o editar un proveedor (devuelve el proveedor guardado al cerrar o nada si se cancela) */
@Component({
  selector: 'app-supplier-dialog',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form
      [formGroup]="form"
      (ngSubmit)="save()"
      novalidate
      class="card flex max-h-[calc(100dvh-2rem)] w-[min(34rem,calc(100vw-2rem))] flex-col shadow-2xl"
      aria-labelledby="supplier-dialog-title"
    >
      <div class="border-b border-line px-6 py-4">
        <h2 id="supplier-dialog-title" class="text-lg font-bold">{{ supplier ? 'Editar proveedor' : 'Nuevo proveedor' }}</h2>
      </div>

      <div class="space-y-5 overflow-y-auto px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }

        <div class="grid gap-5 sm:grid-cols-[13rem_1fr]">
          <div>
            <label for="supplier-doc-type" class="field-label">Tipo de documento</label>
            <select id="supplier-doc-type" formControlName="documentType" class="field-input">
              @for (type of documentTypes; track type.value) {
                <option [value]="type.value">{{ type.label }}</option>
              }
            </select>
          </div>
          <div>
            <label for="supplier-doc" class="field-label">Número</label>
            <input id="supplier-doc" formControlName="documentNumber" autocomplete="off" class="field-input" [attr.aria-invalid]="invalid('documentNumber')" />
            @if (invalid('documentNumber')) {
              <p class="field-error">{{ serverErrors()['documentNumber'] ?? documentMessage() }}</p>
            }
          </div>
        </div>

        <div>
          <label for="supplier-name" class="field-label">Nombre o razón social</label>
          <input id="supplier-name" formControlName="name" class="field-input" [attr.aria-invalid]="invalid('name')" />
          @if (invalid('name')) {
            <p class="field-error">{{ serverErrors()['name'] ?? 'Escribe el nombre (máximo 100 caracteres).' }}</p>
          }
        </div>

        <div class="grid gap-5 sm:grid-cols-2">
          <div>
            <label for="supplier-contact" class="field-label">Contacto <span class="font-normal text-faint">(opcional)</span></label>
            <input id="supplier-contact" formControlName="contactName" class="field-input" [attr.aria-invalid]="invalid('contactName')" />
          </div>
          <div>
            <label for="supplier-phone" class="field-label">Teléfono <span class="font-normal text-faint">(opcional)</span></label>
            <input id="supplier-phone" type="tel" formControlName="phone" class="field-input" [attr.aria-invalid]="invalid('phone')" />
            @if (invalid('phone')) {
              <p class="field-error">{{ serverErrors()['phone'] ?? 'De 7 a 15 dígitos.' }}</p>
            }
          </div>
        </div>

        <div>
          <label for="supplier-email" class="field-label">Correo <span class="font-normal text-faint">(opcional)</span></label>
          <input id="supplier-email" type="email" formControlName="email" class="field-input" [attr.aria-invalid]="invalid('email')" />
          @if (invalid('email')) {
            <p class="field-error">{{ serverErrors()['email'] ?? 'Escribe un correo válido.' }}</p>
          }
        </div>

        <div>
          <label for="supplier-address" class="field-label">Dirección <span class="font-normal text-faint">(opcional)</span></label>
          <input id="supplier-address" formControlName="address" class="field-input" />
        </div>

        <div>
          <label for="supplier-notes" class="field-label">Notas <span class="font-normal text-faint">(opcional)</span></label>
          <textarea id="supplier-notes" formControlName="notes" rows="2" placeholder="Días de entrega, condiciones de pago…" class="field-input resize-none"></textarea>
        </div>

        <label class="flex cursor-pointer items-start gap-3 rounded-xl border border-line p-3.5">
          <input type="checkbox" formControlName="isActive" class="mt-0.5 size-4 accent-accent" />
          <span>
            <span class="block text-sm font-medium">Activo</span>
            <span class="block text-xs text-muted">Si lo desactivas no aparece al registrar compras, pero no se borra.</span>
          </span>
        </label>
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="submit" class="btn-primary" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar' }}</button>
      </div>
    </form>
  `,
})
export class SupplierDialog {
  private readonly http = inject(HttpClient);
  protected readonly supplier = inject<Supplier | null>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Supplier>>(DialogRef);
  protected readonly documentTypes = DOCUMENT_TYPES;

  private readonly s = this.supplier;
  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      documentType: [this.s?.documentType ?? ('NIT' as DocumentType)],
      documentNumber: [this.s?.documentNumber ?? '', [Validators.required]],
      name: [this.s?.name ?? '', [Validators.required, Validators.maxLength(100)]],
      contactName: [this.s?.contactName ?? '', [Validators.maxLength(80)]],
      phone: [this.s?.phone ?? '', [Validators.pattern(/^\+?[0-9][0-9 -]{5,18}[0-9]$/)]],
      email: [this.s?.email ?? '', [Validators.email, Validators.maxLength(256)]],
      address: [this.s?.address ?? '', [Validators.maxLength(120)]],
      notes: [this.s?.notes ?? '', [Validators.maxLength(500)]],
      isActive: [this.s?.isActive ?? true],
    },
    { validators: [documentValidator] },
  );

  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly documentMessage = computed(() => documentError(this.values().documentType ?? 'NIT'));

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  protected invalid(control: Field): boolean {
    const c = this.form.controls[control];
    const documentBad = control === 'documentNumber' && this.form.hasError('document') && c.touched;
    return (c.invalid && c.touched) || documentBad || !!this.serverErrors()[control];
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const body: SupplierUpsert = {
      documentType: raw.documentType,
      documentNumber: raw.documentNumber.trim(),
      name: raw.name.trim(),
      contactName: optional(raw.contactName),
      phone: optional(raw.phone),
      email: optional(raw.email),
      address: optional(raw.address),
      notes: optional(raw.notes),
      isActive: raw.isActive,
    };
    const request = this.s
      ? this.http.put<Supplier>(`${API_BASE}/suppliers/${this.s.id}`, body, { context: silentErrors() })
      : this.http.post<Supplier>(`${API_BASE}/suppliers`, body, { context: silentErrors() });

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
