import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroPlus, heroXMark } from '@ng-icons/heroicons/outline';
import { API_BASE, getErrorMessage, silentErrors } from '../../../core/http/api';
import { changeFor } from '../../../shared/cart';
import { formatPrice } from '../../../shared/format';
import { CreateSaleRequest, PAYMENT_METHODS, PaymentMethod, Sale } from '../business.models';

export interface PaymentDialogData {
  total: number;
  /** pedido sin los pagos (los agrega este dialogo) */
  request: Omit<CreateSaleRequest, 'payments' | 'cashTendered' | 'idempotencyKey'>;
}

interface PaymentRow {
  method: PaymentMethod;
  amount: number;
}

/** cobro (registra la venta y devuelve la venta del servidor al cerrar) */
@Component({
  selector: 'app-payment-dialog',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroPlus, heroXMark })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="charge()" class="card flex w-[min(30rem,calc(100vw-2rem))] flex-col shadow-2xl" aria-labelledby="pay-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="pay-title" class="text-lg font-bold">Cobrar</h2>
        <p class="mt-1 text-3xl font-extrabold tabular-nums">{{ money(data.total) }}</p>
      </div>

      <div class="space-y-5 px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }

        <fieldset class="space-y-3">
          <legend class="field-label">Medio de pago</legend>
          @for (row of rows(); track $index) {
            <div class="flex gap-2">
              <label class="sr-only" [for]="'method-' + $index">Medio de pago {{ $index + 1 }}</label>
              <select [id]="'method-' + $index" class="field-input w-40" [ngModel]="row.method" [name]="'method-' + $index" (ngModelChange)="setMethod($index, $event)">
                @for (m of methods; track m.value) {
                  <option [value]="m.value" [disabled]="m.value === 'Cash' && hasCashElsewhere($index)">{{ m.label }}</option>
                }
              </select>
              <label class="sr-only" [for]="'amount-' + $index">Valor</label>
              <input
                [id]="'amount-' + $index"
                type="number"
                inputmode="decimal"
                min="0"
                class="field-input flex-1 tabular-nums"
                [ngModel]="row.amount"
                [name]="'amount-' + $index"
                (ngModelChange)="setAmount($index, $event)"
              />
              @if (rows().length > 1) {
                <button type="button" class="btn-ghost px-2.5" (click)="removeRow($index)" [attr.aria-label]="'Quitar pago ' + ($index + 1)">
                  <ng-icon name="heroXMark" size="18" />
                </button>
              }
            </div>
          }
          @if (rows().length < 3) {
            <button type="button" class="btn-ghost px-2 text-sm" (click)="addRow()">
              <ng-icon name="heroPlus" size="16" /> Pago mixto
            </button>
          }
          @if (pending() !== 0) {
            <p class="text-sm" [class]="pending() > 0 ? 'text-danger' : 'text-danger'">
              {{ pending() > 0 ? 'Falta ' + money(pending()) : 'Sobra ' + money(-pending()) }} para cuadrar con el total.
            </p>
          }
        </fieldset>

        @if (cashAmount() > 0) {
          <div>
            <label for="tendered" class="field-label">Efectivo recibido</label>
            <input id="tendered" type="number" inputmode="decimal" min="0" class="field-input tabular-nums" [ngModel]="tendered()" name="tendered" (ngModelChange)="tendered.set(toNumber($event))" />
            <div class="mt-2 flex flex-wrap gap-2">
              @for (bill of quickBills(); track bill) {
                <button type="button" class="btn-secondary px-3 py-1.5 text-xs tabular-nums" (click)="tendered.set(bill)">{{ bill === cashAmount() ? 'Exacto' : money(bill) }}</button>
              }
            </div>
            <p class="mt-3 text-sm" aria-live="polite">
              @if (change() === null) {
                <span class="font-medium text-danger">El efectivo recibido no alcanza.</span>
              } @else {
                Cambio: <strong class="text-lg tabular-nums">{{ money(change() ?? 0) }}</strong>
              }
            </p>
          </div>
        }
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()" [disabled]="saving()">Cancelar</button>
        <button type="submit" class="btn-primary px-6" [disabled]="saving() || !canCharge()">{{ saving() ? 'Registrando…' : 'Confirmar cobro' }}</button>
      </div>
    </form>
  `,
})
export class PaymentDialog {
  private readonly http = inject(HttpClient);
  protected readonly data = inject<PaymentDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Sale>>(DialogRef);
  protected readonly methods = PAYMENT_METHODS;
  protected readonly money = formatPrice;

  // una clave por cobro (si la conexion falla y se reintenta el servidor devuelve la misma venta y no cobra dos veces)
  private readonly idempotencyKey = crypto.randomUUID();

  protected readonly rows = signal<PaymentRow[]>([{ method: 'Cash', amount: this.data.total }]);
  protected readonly tendered = signal(this.data.total);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly paid = computed(() => Math.round(this.rows().reduce((sum, r) => sum + r.amount, 0) * 100) / 100);
  protected readonly pending = computed(() => Math.round((this.data.total - this.paid()) * 100) / 100);
  protected readonly cashAmount = computed(() => this.rows().find((r) => r.method === 'Cash')?.amount ?? 0);
  protected readonly change = computed(() => changeFor(this.cashAmount(), this.tendered()));
  protected readonly canCharge = computed(
    () => this.pending() === 0 && this.rows().every((r) => r.amount > 0) && (this.cashAmount() === 0 || this.change() !== null),
  );

  /** billetes sugeridos por encima de lo que se paga en efectivo */
  protected readonly quickBills = computed(() => {
    const cash = this.cashAmount();
    const bills = [10000, 20000, 50000, 100000].map((b) => Math.ceil(cash / b) * b).filter((b) => b > cash);
    return [cash, ...new Set(bills)].slice(0, 4);
  });

  protected toNumber(value: unknown): number {
    const n = Number(value);
    return Number.isFinite(n) && n >= 0 ? n : 0;
  }

  protected hasCashElsewhere(index: number): boolean {
    return this.rows().some((r, i) => i !== index && r.method === 'Cash');
  }

  protected setMethod(index: number, method: PaymentMethod): void {
    this.rows.update((rows) => rows.map((r, i) => (i === index ? { ...r, method } : r)));
  }

  protected setAmount(index: number, value: unknown): void {
    this.rows.update((rows) => rows.map((r, i) => (i === index ? { ...r, amount: this.toNumber(value) } : r)));
  }

  protected addRow(): void {
    const used = new Set(this.rows().map((r) => r.method));
    const method = (['Card', 'Transfer', 'Cash'] as PaymentMethod[]).find((m) => !used.has(m)) ?? 'Card';
    this.rows.update((rows) => [...rows, { method, amount: Math.max(0, this.pending()) }]);
  }

  protected removeRow(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected charge(): void {
    if (!this.canCharge() || this.saving()) return;
    const body: CreateSaleRequest = {
      ...this.data.request,
      idempotencyKey: this.idempotencyKey,
      payments: this.rows().map((r) => ({ method: r.method, amount: r.amount })),
      cashTendered: this.cashAmount() > 0 ? this.tendered() : 0,
    };
    this.saving.set(true);
    this.error.set(null);
    this.http.post<Sale>(`${API_BASE}/sales`, body, { context: silentErrors() }).subscribe({
      next: (sale) => this.ref.close(sale),
      error: (e: unknown) => {
        this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}
