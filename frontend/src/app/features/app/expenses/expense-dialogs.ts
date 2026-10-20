import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { localIsoDate } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import {
  CreateExpenseRequest,
  EXPENSE_CATEGORIES,
  Expense,
  ExpenseCategory,
  PAYMENT_METHODS,
  PaySupplierRequest,
  PaymentMethod,
  SupplierPayment,
} from '../business.models';

export interface NewExpenseDialogData {
  /** solo salida de efectivo de la caja abierta (el cajero y el punto de venta) */
  cashOnly: boolean;
}

/** registrar un gasto (cierra con el gasto creado) */
@Component({
  selector: 'app-new-expense-dialog',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="save()" class="card flex max-h-[calc(100dvh-2rem)] w-[min(30rem,calc(100vw-2rem))] flex-col !shadow-2xl" aria-labelledby="expense-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="expense-title" class="text-lg font-bold">{{ data.cashOnly ? 'Salida de caja' : 'Nuevo gasto' }}</h2>
        <p class="text-sm text-muted">{{ data.cashOnly ? 'Efectivo que sacas del cajón para pagar algo del negocio.' : 'Lo que gasta el negocio fuera de las compras de mercancía.' }}</p>
      </div>

      <div class="space-y-4 overflow-y-auto px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }
        <div>
          <label for="expense-description" class="field-label">Descripción</label>
          <input id="expense-description" class="field-input" maxlength="150" placeholder="Hielo, gas, domiciliario…" [(ngModel)]="description" name="description" required [attr.aria-invalid]="!!fieldErrors()['description']" />
          @if (fieldErrors()['description']) { <p class="field-error">{{ fieldErrors()['description'] }}</p> }
        </div>
        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label for="expense-amount" class="field-label">Valor</label>
            <input id="expense-amount" type="number" inputmode="decimal" min="1" step="100" class="field-input tabular-nums" [(ngModel)]="amount" name="amount" required [attr.aria-invalid]="!!fieldErrors()['amount']" />
            @if (fieldErrors()['amount']) { <p class="field-error">{{ fieldErrors()['amount'] }}</p> }
          </div>
          <div>
            <label for="expense-category" class="field-label">Categoría</label>
            <select id="expense-category" class="field-input" [(ngModel)]="category" name="category">
              @for (c of categories; track c.value) {
                <option [value]="c.value">{{ c.label }}</option>
              }
            </select>
          </div>
        </div>

        @if (!data.cashOnly) {
          <div class="grid gap-4 sm:grid-cols-2">
            <div>
              <label for="expense-method" class="field-label">Medio de pago</label>
              <select id="expense-method" class="field-input" [ngModel]="method()" (ngModelChange)="setMethod($event)" name="method">
                @for (m of methods; track m.value) {
                  <option [value]="m.value">{{ m.label }}</option>
                }
              </select>
            </div>
            <div>
              <label for="expense-date" class="field-label">Fecha</label>
              <input id="expense-date" type="date" class="field-input" [max]="today" [(ngModel)]="date" name="date" [disabled]="fromRegister()" [attr.aria-invalid]="!!fieldErrors()['date']" />
              @if (fieldErrors()['date']) { <p class="field-error">{{ fieldErrors()['date'] }}</p> }
            </div>
          </div>
          @if (method() === 'Cash') {
            <label class="flex items-start gap-3 rounded-xl border border-line px-3.5 py-3 text-sm">
              <input type="checkbox" class="mt-0.5 size-4 accent-accent" [ngModel]="fromRegister()" (ngModelChange)="fromRegister.set($event)" name="fromRegister" />
              <span>Sale de mi caja abierta<span class="block text-xs text-muted">Se resta del efectivo esperado al cerrar el turno (solo hoy).</span></span>
            </label>
          }
        } @else {
          <p class="rounded-xl bg-surface-2 px-3.5 py-2.5 text-xs text-muted">Se registra en efectivo con la fecha de hoy y se resta del efectivo esperado al cerrar tu caja.</p>
        }
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()" [disabled]="saving()">Cancelar</button>
        <button type="submit" class="btn-primary" [disabled]="saving() || !description.trim() || !(amount > 0)">{{ saving() ? 'Guardando…' : 'Registrar ' + (amount > 0 ? money(amount) : '') }}</button>
      </div>
    </form>
  `,
})
export class NewExpenseDialog {
  private readonly http = inject(HttpClient);
  protected readonly data = inject<NewExpenseDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Expense>>(DialogRef);
  protected readonly money = formatPrice;
  protected readonly today = localIsoDate();
  protected readonly categories = EXPENSE_CATEGORIES;
  protected readonly methods = PAYMENT_METHODS;

  protected description = '';
  protected amount = 0;
  protected category: ExpenseCategory = 'Supplies';
  protected date = this.today;
  protected readonly method = signal<PaymentMethod>(this.data.cashOnly ? 'Cash' : 'Transfer');
  protected readonly fromRegister = signal(this.data.cashOnly);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});

  protected setMethod(method: PaymentMethod): void {
    this.method.set(method);
    if (method !== 'Cash') this.fromRegister.set(false);
  }

  protected save(): void {
    const fromRegister = this.data.cashOnly || (this.method() === 'Cash' && this.fromRegister());
    const body: CreateExpenseRequest = {
      date: fromRegister ? null : this.date || null,
      category: this.category,
      description: this.description.trim(),
      amount: Number(this.amount),
      method: this.data.cashOnly ? 'Cash' : this.method(),
      fromCashRegister: fromRegister,
    };
    this.saving.set(true);
    this.error.set(null);
    this.http.post<Expense>(`${API_BASE}/expenses`, body, { context: silentErrors() }).subscribe({
      next: (expense) => this.ref.close(expense),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.fieldErrors.set(fields);
        this.error.set(Object.keys(fields).length ? null : getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}

export interface PaySupplierDialogData {
  purchaseId: string;
  number: number;
  supplierName: string;
  balance: number;
}

/** abono o pago total de una compra a credito (cierra con el pago registrado) */
@Component({
  selector: 'app-pay-supplier-dialog',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="save()" class="card flex max-h-[calc(100dvh-2rem)] w-[min(30rem,calc(100vw-2rem))] flex-col !shadow-2xl" aria-labelledby="pay-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="pay-title" class="text-lg font-bold">Pagar compra N.° {{ data.number }}</h2>
        <p class="text-sm text-muted">{{ data.supplierName }} · saldo {{ money(data.balance) }}</p>
      </div>

      <div class="space-y-4 overflow-y-auto px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }
        <div>
          <label for="pay-amount" class="field-label">Valor del pago</label>
          <div class="flex gap-2">
            <input id="pay-amount" type="number" inputmode="decimal" min="1" [max]="data.balance" step="100" class="field-input tabular-nums" [(ngModel)]="amount" name="amount" required [attr.aria-invalid]="!!fieldErrors()['amount']" />
            <button type="button" class="btn-secondary shrink-0" (click)="amount = data.balance">Todo el saldo</button>
          </div>
          @if (fieldErrors()['amount']) { <p class="field-error">{{ fieldErrors()['amount'] }}</p> }
          @if (amount > data.balance) { <p class="field-error">Supera el saldo ({{ money(data.balance) }}).</p> }
        </div>
        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label for="pay-method" class="field-label">Medio de pago</label>
            <select id="pay-method" class="field-input" [ngModel]="method()" (ngModelChange)="setMethod($event)" name="method">
              @for (m of methods; track m.value) {
                <option [value]="m.value">{{ m.label }}</option>
              }
            </select>
          </div>
          <div>
            <label for="pay-date" class="field-label">Fecha</label>
            <input id="pay-date" type="date" class="field-input" [max]="today" [(ngModel)]="date" name="date" [disabled]="fromRegister()" [attr.aria-invalid]="!!fieldErrors()['date']" />
            @if (fieldErrors()['date']) { <p class="field-error">{{ fieldErrors()['date'] }}</p> }
          </div>
        </div>
        <div>
          <label for="pay-reference" class="field-label">Referencia <span class="font-normal text-faint">(opcional)</span></label>
          <input id="pay-reference" class="field-input" maxlength="40" placeholder="N.° de la transferencia o recibo" [(ngModel)]="reference" name="reference" />
        </div>
        @if (method() === 'Cash') {
          <label class="flex items-start gap-3 rounded-xl border border-line px-3.5 py-3 text-sm">
            <input type="checkbox" class="mt-0.5 size-4 accent-accent" [ngModel]="fromRegister()" (ngModelChange)="fromRegister.set($event)" name="fromRegister" />
            <span>Sale de mi caja abierta<span class="block text-xs text-muted">Se resta del efectivo esperado al cerrar el turno (solo hoy).</span></span>
          </label>
        }
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()" [disabled]="saving()">Cancelar</button>
        <button type="submit" class="btn-primary" [disabled]="saving() || !(amount > 0) || amount > data.balance">{{ saving() ? 'Registrando…' : 'Registrar pago' }}</button>
      </div>
    </form>
  `,
})
export class PaySupplierDialog {
  private readonly http = inject(HttpClient);
  protected readonly data = inject<PaySupplierDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<SupplierPayment>>(DialogRef);
  protected readonly money = formatPrice;
  protected readonly today = localIsoDate();
  protected readonly methods = PAYMENT_METHODS;

  protected amount = this.data.balance;
  protected date = this.today;
  protected reference = '';
  protected readonly method = signal<PaymentMethod>('Transfer');
  protected readonly fromRegister = signal(false);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});

  protected setMethod(method: PaymentMethod): void {
    this.method.set(method);
    if (method !== 'Cash') this.fromRegister.set(false);
  }

  protected save(): void {
    const fromRegister = this.method() === 'Cash' && this.fromRegister();
    const body: PaySupplierRequest = {
      amount: Number(this.amount),
      method: this.method(),
      date: fromRegister ? null : this.date || null,
      fromCashRegister: fromRegister,
      reference: this.reference.trim() || null,
    };
    this.saving.set(true);
    this.error.set(null);
    this.http.post<SupplierPayment>(`${API_BASE}/purchases/${this.data.purchaseId}/payments`, body, { context: silentErrors() }).subscribe({
      next: (payment) => this.ref.close(payment),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.fieldErrors.set(fields);
        this.error.set(Object.keys(fields).length ? null : getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}
