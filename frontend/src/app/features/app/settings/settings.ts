import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { normalizeNit } from '../../../shared/nit';
import { BusinessSettings, TAX_OPTIONS, TaxType, UpdateBusiness } from '../business.models';

export const TIME_ZONES = [
  { id: 'America/Bogota', label: 'Colombia (Bogotá)' },
  { id: 'America/Lima', label: 'Perú (Lima)' },
  { id: 'America/Guayaquil', label: 'Ecuador (Guayaquil)' },
  { id: 'America/Panama', label: 'Panamá' },
  { id: 'America/Mexico_City', label: 'México (Ciudad de México)' },
  { id: 'America/Santiago', label: 'Chile (Santiago)' },
  { id: 'America/Argentina/Buenos_Aires', label: 'Argentina (Buenos Aires)' },
];

type Field = 'name' | 'taxId' | 'address' | 'city' | 'phone' | 'timeZone' | 'defaultTax' | 'receiptFooter';

/** NIT opcional pero si viene debe tener un digito de verificacion correcto */
function nitValidator(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value.trim();
  return value === '' || normalizeNit(value) ? null : { nit: true };
}

@Component({
  selector: 'app-settings',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings.html',
})
export class Settings {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly toasts = inject(ToastService);

  protected readonly business = httpResource<BusinessSettings>(() => `${API_BASE}/business`);
  protected readonly timeZones = TIME_ZONES;
  protected readonly taxOptions = TAX_OPTIONS;

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(80)]],
    taxId: ['', [nitValidator]],
    address: ['', [Validators.maxLength(120)]],
    city: ['', [Validators.maxLength(60)]],
    phone: ['', [Validators.pattern(/^\+?[0-9][0-9 -]{5,18}[0-9]$/)]],
    timeZone: ['America/Bogota', [Validators.required]],
    defaultTax: ['Inc8' as TaxType, [Validators.required]],
    receiptFooter: ['', [Validators.maxLength(200)]],
  });

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  constructor() {
    // carga los datos en el formulario cuando llegan de la API
    effect(() => {
      if (!this.business.hasValue()) return;
      const b = this.business.value();
      this.form.reset({
        name: b.name,
        taxId: b.taxId ?? '',
        address: b.address ?? '',
        city: b.city ?? '',
        phone: b.phone ?? '',
        timeZone: b.timeZone,
        defaultTax: b.defaultTax,
        receiptFooter: b.receiptFooter ?? '',
      });
    });

    this.form.valueChanges.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
      if (Object.keys(this.serverErrors()).length > 0) this.serverErrors.set({});
    });
  }

  protected invalid(control: Field): boolean {
    const c = this.form.controls[control];
    return (c.invalid && c.touched) || !!this.serverErrors()[control];
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const optional = (value: string): string | null => value.trim() || null;
    const body: UpdateBusiness = {
      name: raw.name.trim(),
      taxId: optional(raw.taxId),
      address: optional(raw.address),
      city: optional(raw.city),
      phone: optional(raw.phone),
      timeZone: raw.timeZone,
      defaultTax: raw.defaultTax,
      receiptFooter: optional(raw.receiptFooter),
    };

    this.saving.set(true);
    this.error.set(null);
    this.http.put<BusinessSettings>(`${API_BASE}/business`, body, { context: silentErrors() }).subscribe({
      next: (saved) => {
        this.business.set(saved);
        this.auth.updateTenant({ id: saved.id, name: saved.name });
        this.toasts.success('Cambios guardados.');
        this.saving.set(false);
      },
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}
