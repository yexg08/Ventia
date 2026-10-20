import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { Product, StockAdjustment, StockAdjustmentKind, StockMovement, StockMovementType } from '../business.models';

const KINDS: { value: StockAdjustmentKind; label: string; hint: string }[] = [
  { value: 'In', label: 'Entrada', hint: 'Llegaron unidades' },
  { value: 'Out', label: 'Salida', hint: 'Merma, daño o consumo' },
  { value: 'Count', label: 'Conteo', hint: 'Dejar lo que contaste' },
];

const REASONS: Record<StockAdjustmentKind, string[]> = {
  In: ['Llegó mercancía', 'Devolución de un cliente'],
  Out: ['Producto dañado', 'Producto vencido', 'Consumo interno'],
  Count: ['Conteo físico'],
};

/** ajuste de stock de un producto (devuelve el producto actualizado al cerrar) */
@Component({
  selector: 'app-stock-adjustment-dialog',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()" novalidate class="card w-[min(30rem,calc(100vw-2rem))] !shadow-2xl" aria-labelledby="stock-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="stock-title" class="text-lg font-bold">Ajustar stock</h2>
        <p class="text-sm text-muted">{{ product.name }} · hay <strong class="text-ink">{{ product.stock }}</strong> unidades</p>
      </div>

      <div class="space-y-5 px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }

        <fieldset>
          <legend class="field-label">Tipo de ajuste</legend>
          <div class="grid grid-cols-3 gap-2">
            @for (kind of kinds; track kind.value) {
              <label class="flex cursor-pointer flex-col rounded-xl border border-line p-3 text-center transition has-[:checked]:border-accent has-[:checked]:bg-surface-2">
                <input type="radio" formControlName="kind" [value]="kind.value" class="sr-only" />
                <span class="text-sm font-semibold">{{ kind.label }}</span>
                <span class="text-xs text-muted">{{ kind.hint }}</span>
              </label>
            }
          </div>
        </fieldset>

        <div>
          <label for="stock-quantity" class="field-label">{{ values().kind === 'Count' ? 'Unidades contadas' : 'Unidades' }}</label>
          <input id="stock-quantity" type="number" inputmode="numeric" step="1" [min]="values().kind === 'Count' ? 0 : 1" formControlName="quantity" class="field-input" [attr.aria-invalid]="invalid('quantity')" />
          @if (invalid('quantity')) {
            <p class="field-error">{{ serverErrors()['quantity'] ?? 'Escribe una cantidad válida.' }}</p>
          } @else {
            <p class="mt-1.5 text-xs text-faint">Quedarían {{ preview() }} unidades.</p>
          }
        </div>

        <div>
          <label for="stock-reason" class="field-label">Motivo</label>
          <input id="stock-reason" formControlName="reason" list="stock-reasons" class="field-input" [attr.aria-invalid]="invalid('reason')" />
          <datalist id="stock-reasons">
            @for (reason of reasons(); track reason) {
              <option [value]="reason"></option>
            }
          </datalist>
          @if (invalid('reason')) {
            <p class="field-error">{{ serverErrors()['reason'] ?? 'Escribe el motivo (máximo 120 caracteres).' }}</p>
          }
        </div>
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="submit" class="btn-primary" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar ajuste' }}</button>
      </div>
    </form>
  `,
})
export class StockAdjustmentDialog {
  private readonly http = inject(HttpClient);
  protected readonly product = inject<Product>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Product>>(DialogRef);
  protected readonly kinds = KINDS;

  protected readonly form = inject(NonNullableFormBuilder).group({
    kind: ['In' as StockAdjustmentKind],
    quantity: [1, [Validators.required, Validators.min(0), Validators.max(1_000_000), Validators.pattern(/^\d+$/)]],
    reason: ['Llegó mercancía', [Validators.required, Validators.maxLength(120)]],
  });

  protected readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly reasons = computed(() => REASONS[this.values().kind ?? 'In']);

  /** stock que quedaria con el ajuste (solo informativo y la API decide) */
  protected readonly preview = computed(() => {
    const quantity = Number(this.values().quantity) || 0;
    switch (this.values().kind) {
      case 'Out':
        return this.product.stock - quantity;
      case 'Count':
        return quantity;
      default:
        return this.product.stock + quantity;
    }
  });

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  constructor() {
    // al cambiar el tipo se sugiere un motivo acorde
    this.form.controls.kind.valueChanges.subscribe((kind) => this.form.controls.reason.setValue(REASONS[kind][0]));
  }

  protected invalid(control: 'quantity' | 'reason'): boolean {
    const c = this.form.controls[control];
    return (c.invalid && c.touched) || !!this.serverErrors()[control];
  }

  protected save(): void {
    const raw = this.form.getRawValue();
    if (this.form.invalid || (raw.kind !== 'Count' && Number(raw.quantity) < 1)) {
      this.form.controls.quantity.setErrors(this.form.controls.quantity.errors ?? { min: true });
      this.form.markAllAsTouched();
      return;
    }
    const body: StockAdjustment = { kind: raw.kind, quantity: Number(raw.quantity), reason: raw.reason.trim() };
    this.saving.set(true);
    this.error.set(null);
    this.http.post<Product>(`${API_BASE}/products/${this.product.id}/stock-adjustments`, body, { context: silentErrors() }).subscribe({
      next: (updated) => this.ref.close(updated),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}

const MOVEMENT_LABELS: Record<StockMovementType, string> = {
  Initial: 'Stock inicial',
  In: 'Entrada',
  Out: 'Salida',
  Count: 'Conteo',
};

/** historial de movimientos de inventario de un producto (solo el dueno) */
@Component({
  selector: 'app-stock-movements-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card flex max-h-[calc(100dvh-2rem)] w-[min(42rem,calc(100vw-2rem))] flex-col !shadow-2xl" role="dialog" aria-labelledby="movements-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="movements-title" class="text-lg font-bold">Movimientos de inventario</h2>
        <p class="text-sm text-muted">{{ product.name }} · stock actual {{ product.stock }}</p>
      </div>

      <div class="overflow-y-auto">
        @if (movements.isLoading() && movements.value().length === 0) {
          <div class="m-6 h-32 skeleton rounded-xl"></div>
        } @else if (movements.error()) {
          <p class="p-6 text-center text-sm text-muted">No se pudo cargar el historial.</p>
        } @else if (movements.value().length === 0) {
          <p class="p-6 text-center text-sm text-muted">Este producto aún no tiene movimientos.</p>
        } @else {
          <table class="w-full text-left text-sm">
            <thead class="sticky top-0 border-b border-line bg-surface-2 text-xs tracking-wider text-muted uppercase">
              <tr>
                <th scope="col" class="px-5 py-2.5 font-semibold">Fecha</th>
                <th scope="col" class="px-5 py-2.5 font-semibold">Movimiento</th>
                <th scope="col" class="px-5 py-2.5 text-right font-semibold">Cantidad</th>
                <th scope="col" class="px-5 py-2.5 text-right font-semibold">Quedó</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-line">
              @for (m of movements.value(); track m.id) {
                <tr>
                  <td class="px-5 py-3 whitespace-nowrap text-muted">{{ dateTime(m.createdAt) }}</td>
                  <td class="px-5 py-3">
                    <p class="font-medium">{{ label(m.type) }}</p>
                    <p class="text-xs text-muted">{{ m.reason ?? '—' }} · {{ m.userName }}</p>
                  </td>
                  <td class="px-5 py-3 text-right font-semibold tabular-nums" [class]="m.quantity < 0 ? 'text-danger' : 'text-success'">
                    {{ m.quantity > 0 ? '+' : '' }}{{ m.quantity }}
                  </td>
                  <td class="px-5 py-3 text-right tabular-nums">{{ m.stockAfter }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      <div class="flex justify-end border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()">Cerrar</button>
      </div>
    </div>
  `,
})
export class StockMovementsDialog {
  protected readonly product = inject<Product>(DIALOG_DATA);
  protected readonly ref = inject(DialogRef);
  protected readonly movements = httpResource<StockMovement[]>(() => `${API_BASE}/products/${this.product.id}/movements`, {
    defaultValue: [],
  });

  protected label(type: StockMovementType): string {
    return MOVEMENT_LABELS[type];
  }

  protected dateTime(iso: string): string {
    return new Intl.DateTimeFormat('es-CO', { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
  }
}
