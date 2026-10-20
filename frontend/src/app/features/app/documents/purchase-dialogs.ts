import { Dialog, DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroPlus, heroTrash } from '@ng-icons/heroicons/outline';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { filter } from 'rxjs';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatIsoDate, localIsoDate } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import { VoidDialog, VoidDialogData } from '../../../shared/void-dialog';
import { CreatePurchaseRequest, PAYMENT_METHODS, PaymentMethod, Product, Purchase, Supplier, SupplierPayment } from '../business.models';
import { PaySupplierDialog, PaySupplierDialogData } from '../expenses/expense-dialogs';

interface DraftLine {
  key: string;
  productId: string;
  quantity: number;
  unitCost: number;
}

/** nueva compra a un proveedor (cierra con la compra registrada) */
@Component({
  selector: 'app-new-purchase-dialog',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroPlus, heroTrash })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="save()" class="card flex max-h-[calc(100dvh-2rem)] w-[min(44rem,calc(100vw-2rem))] flex-col shadow-2xl" aria-labelledby="purchase-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="purchase-title" class="text-lg font-bold">Nueva compra</h2>
        <p class="text-sm text-muted">Sube el stock de cada producto y recalcula su costo promedio.</p>
      </div>

      <div class="space-y-5 overflow-y-auto px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }

        <div class="grid gap-4 sm:grid-cols-3">
          <div class="sm:col-span-1">
            <label for="purchase-supplier" class="field-label">Proveedor</label>
            <select id="purchase-supplier" class="field-input" [(ngModel)]="supplierId" name="supplierId" required [attr.aria-invalid]="!!fieldErrors()['supplierId']">
              <option value="" disabled>Elige uno</option>
              @for (s of activeSuppliers(); track s.id) {
                <option [value]="s.id">{{ s.name }}</option>
              }
            </select>
            @if (fieldErrors()['supplierId']) { <p class="field-error">{{ fieldErrors()['supplierId'] }}</p> }
          </div>
          <div>
            <label for="purchase-invoice" class="field-label">Factura del proveedor <span class="font-normal text-faint">(opcional)</span></label>
            <input id="purchase-invoice" class="field-input" maxlength="30" [(ngModel)]="invoice" name="invoice" />
          </div>
          <div>
            <label for="purchase-date" class="field-label">Fecha</label>
            <input id="purchase-date" type="date" class="field-input" [max]="today" [(ngModel)]="date" name="date" required [attr.aria-invalid]="!!fieldErrors()['date']" />
            @if (fieldErrors()['date']) { <p class="field-error">{{ fieldErrors()['date'] }}</p> }
          </div>
        </div>

        @if (trackedProducts().length === 0 && !products.isLoading()) {
          <p class="rounded-xl border border-line bg-surface-2 px-4 py-3 text-sm text-muted">
            Ningún producto lleva inventario. Actívalo en el producto para poder registrar sus compras.
          </p>
        }

        <fieldset>
          <legend class="field-label">Productos</legend>
          <div class="space-y-2">
            @for (line of lines(); track line.key; let i = $index) {
              <div class="grid grid-cols-[1fr_5rem_7rem_auto] items-center gap-2">
                <label class="sr-only" [for]="'p-' + line.key">Producto {{ i + 1 }}</label>
                <select [id]="'p-' + line.key" class="field-input" [ngModel]="line.productId" [name]="'p-' + line.key" (ngModelChange)="setProduct(line, $event)">
                  <option value="" disabled>Producto</option>
                  @for (p of trackedProducts(); track p.id) {
                    <option [value]="p.id">{{ p.name }}</option>
                  }
                </select>
                <label class="sr-only" [for]="'q-' + line.key">Cantidad</label>
                <input [id]="'q-' + line.key" type="number" min="1" step="1" class="field-input tabular-nums" [ngModel]="line.quantity" [name]="'q-' + line.key" (ngModelChange)="patch(line, { quantity: num($event) })" />
                <label class="sr-only" [for]="'c-' + line.key">Costo unitario</label>
                <input [id]="'c-' + line.key" type="number" min="0" step="50" class="field-input tabular-nums" [ngModel]="line.unitCost" [name]="'c-' + line.key" (ngModelChange)="patch(line, { unitCost: num($event) })" />
                <button type="button" class="btn-ghost px-2.5" (click)="removeLine(line)" [disabled]="lines().length === 1" [attr.aria-label]="'Quitar línea ' + (i + 1)"><ng-icon name="heroTrash" size="17" /></button>
              </div>
            }
          </div>
          <p class="mt-1 grid grid-cols-[1fr_5rem_7rem_auto] gap-2 text-xs text-faint" aria-hidden="true"><span></span><span>Cantidad</span><span>Costo unitario</span><span class="w-10"></span></p>
          <button type="button" class="btn-ghost mt-1 px-2 text-sm" (click)="addLine()"><ng-icon name="heroPlus" size="16" /> Otro producto</button>
        </fieldset>

        <fieldset>
          <legend class="field-label">Pago</legend>
          <div class="grid grid-cols-2 gap-1 rounded-xl bg-surface-2 p-1" role="radiogroup" aria-label="Forma de pago">
            <button type="button" role="radio" class="rounded-lg px-2 py-1.5 text-sm font-semibold transition" [attr.aria-checked]="!paidNow()" [class]="!paidNow() ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink'" (click)="paidNow.set(false)">A crédito</button>
            <button type="button" role="radio" class="rounded-lg px-2 py-1.5 text-sm font-semibold transition" [attr.aria-checked]="paidNow()" [class]="paidNow() ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink'" (click)="paidNow.set(true)">De contado</button>
          </div>
          @if (paidNow()) {
            <div class="mt-3 flex flex-wrap items-center gap-3">
              <label for="purchase-method" class="sr-only">Medio de pago</label>
              <select id="purchase-method" class="field-input w-auto" [ngModel]="method()" (ngModelChange)="setMethod($event)" name="method">
                @for (m of methods; track m.value) {
                  <option [value]="m.value">{{ m.label }}</option>
                }
              </select>
              @if (method() === 'Cash') {
                <label class="flex items-center gap-2 text-sm">
                  <input type="checkbox" class="size-4 accent-accent" [ngModel]="fromRegister()" (ngModelChange)="fromRegister.set($event)" name="fromRegister" />
                  Sale de mi caja abierta (solo hoy)
                </label>
              }
            </div>
          } @else {
            <p class="mt-2 text-xs text-faint">Queda en cuentas por pagar hasta que le pagues al proveedor.</p>
          }
        </fieldset>

        <div>
          <label for="purchase-notes" class="field-label">Notas <span class="font-normal text-faint">(opcional)</span></label>
          <input id="purchase-notes" class="field-input" maxlength="300" [(ngModel)]="notes" name="notes" />
        </div>
      </div>

      <div class="flex items-center justify-between gap-2 border-t border-line px-6 py-4">
        <p class="text-lg font-extrabold tabular-nums">Total {{ money(total()) }}</p>
        <div class="flex gap-2">
          <button type="button" class="btn-secondary" (click)="ref.close()">Cancelar</button>
          <button type="submit" class="btn-primary" [disabled]="saving() || !valid()">{{ saving() ? 'Registrando…' : 'Registrar compra' }}</button>
        </div>
      </div>
    </form>
  `,
})
export class NewPurchaseDialog {
  private readonly http = inject(HttpClient);
  protected readonly ref = inject<DialogRef<Purchase>>(DialogRef);
  protected readonly money = formatPrice;
  protected readonly today = localIsoDate();
  protected readonly methods = PAYMENT_METHODS;

  private readonly suppliers = httpResource<Supplier[]>(() => `${API_BASE}/suppliers`, { defaultValue: [] });
  protected readonly products = httpResource<Product[]>(() => `${API_BASE}/products`, { defaultValue: [] });
  protected readonly activeSuppliers = computed(() => this.suppliers.value().filter((s) => s.isActive));
  protected readonly trackedProducts = computed(() => this.products.value().filter((p) => p.tracksStock));

  protected supplierId = '';
  protected invoice = '';
  protected date = this.today;
  protected notes = '';
  protected readonly lines = signal<DraftLine[]>([this.newLine()]);
  protected readonly paidNow = signal(false);
  protected readonly method = signal<PaymentMethod>('Transfer');
  protected readonly fromRegister = signal(false);

  protected readonly total = computed(() => this.lines().reduce((sum, l) => sum + l.quantity * l.unitCost, 0));
  protected readonly valid = computed(() =>
    this.lines().every((l) => l.productId && l.quantity >= 1 && Number.isInteger(l.quantity) && l.unitCost >= 0),
  );

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});

  protected num(value: unknown): number {
    const n = Number(value);
    return Number.isFinite(n) && n >= 0 ? n : 0;
  }

  protected setMethod(method: PaymentMethod): void {
    this.method.set(method);
    if (method !== 'Cash') this.fromRegister.set(false);
  }

  private newLine(): DraftLine {
    return { key: crypto.randomUUID(), productId: '', quantity: 1, unitCost: 0 };
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(line: DraftLine): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== line.key));
  }

  protected patch(line: DraftLine, changes: Partial<DraftLine>): void {
    this.lines.update((lines) => lines.map((l) => (l.key === line.key ? { ...l, ...changes } : l)));
  }

  /** al elegir el producto se sugiere su costo actual */
  protected setProduct(line: DraftLine, productId: string): void {
    const cost = this.products.value().find((p) => p.id === productId)?.cost ?? 0;
    this.patch(line, { productId, unitCost: line.unitCost || cost });
  }

  protected save(): void {
    if (!this.supplierId || !this.valid()) {
      this.error.set('Elige el proveedor y completa cada producto con su cantidad y costo.');
      return;
    }
    // una salida de caja siempre es de hoy
    const fromRegister = this.paidNow() && this.method() === 'Cash' && this.fromRegister();
    const body: CreatePurchaseRequest = {
      supplierId: this.supplierId,
      supplierInvoice: this.invoice.trim() || null,
      date: fromRegister ? null : this.date || null,
      notes: this.notes.trim() || null,
      lines: this.lines().map((l) => ({ productId: l.productId, quantity: l.quantity, unitCost: l.unitCost })),
      paidWith: this.paidNow() ? this.method() : null,
      paidFromCashRegister: fromRegister,
    };
    this.saving.set(true);
    this.error.set(null);
    this.http.post<Purchase>(`${API_BASE}/purchases`, body, { context: silentErrors() }).subscribe({
      next: (purchase) => this.ref.close(purchase),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.fieldErrors.set(fields);
        this.error.set(Object.keys(fields).length ? null : getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}

/** detalle de una compra con sus pagos (cierra con true si se anulo o cambio su saldo) */
@Component({
  selector: 'app-purchase-detail-dialog',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card flex max-h-[calc(100dvh-2rem)] w-[min(34rem,calc(100vw-2rem))] flex-col shadow-2xl" role="dialog" aria-labelledby="purchase-detail-title">
      @if (purchase(); as p) {
        <div class="border-b border-line px-6 py-4">
          <h2 id="purchase-detail-title" class="text-lg font-bold">Compra N.° {{ p.number }}</h2>
          <p class="text-sm text-muted">{{ p.supplierName }} · {{ date(p.date) }}{{ p.supplierInvoice ? ' · factura ' + p.supplierInvoice : '' }}</p>
        </div>
        <div class="overflow-y-auto px-6 py-4 text-sm">
          @if (p.status === 'Voided') {
            <p class="mb-3 rounded-xl border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-danger"><strong>ANULADA</strong> por {{ p.voidedBy }}: {{ p.voidReason }}</p>
          }
          <table class="w-full">
            <thead class="border-b border-line text-xs text-muted">
              <tr><th scope="col" class="py-1.5 text-left font-semibold">Producto</th><th scope="col" class="py-1.5 text-right font-semibold">Cant.</th><th scope="col" class="py-1.5 text-right font-semibold">Costo</th><th scope="col" class="py-1.5 text-right font-semibold">Total</th></tr>
            </thead>
            <tbody>
              @for (l of p.lines; track l.id) {
                <tr><td class="py-1.5">{{ l.productName }}</td><td class="py-1.5 text-right tabular-nums">{{ l.quantity }}</td><td class="py-1.5 text-right tabular-nums">{{ money(l.unitCost) }}</td><td class="py-1.5 text-right tabular-nums">{{ money(l.lineTotal) }}</td></tr>
              }
            </tbody>
          </table>
          <p class="mt-3 flex justify-between border-t border-line pt-3 text-base font-bold"><span>Total</span><span class="tabular-nums">{{ money(p.total) }}</span></p>
          @if (p.status === 'Completed') {
            <p class="mt-1 flex justify-between text-muted"><span>Pagado</span><span class="tabular-nums">{{ money(p.paidAmount) }}</span></p>
            <p class="flex justify-between font-semibold" [class.text-danger]="p.balance > 0"><span>Saldo</span><span class="tabular-nums">{{ money(p.balance) }}</span></p>
          }

          @if (p.payments.length) {
            <h3 class="mt-4 text-xs font-semibold tracking-wider text-muted uppercase">Pagos</h3>
            <ul class="mt-1 divide-y divide-line">
              @for (pay of p.payments; track pay.id) {
                <li class="flex items-center gap-3 py-2" [class.opacity-60]="pay.isVoided">
                  <div class="min-w-0 flex-1">
                    <p>{{ date(pay.date) }} · {{ methodLabel(pay.method) }}{{ pay.fromCashRegister ? ' (de la caja)' : '' }}{{ pay.reference ? ' · ' + pay.reference : '' }}</p>
                    @if (pay.isVoided) {
                      <p class="text-xs text-danger">Anulado por {{ pay.voidedBy }}: {{ pay.voidReason }}</p>
                    } @else {
                      <p class="text-xs text-faint">{{ pay.createdByName }}</p>
                    }
                  </div>
                  <span class="font-semibold tabular-nums" [class.line-through]="pay.isVoided">{{ money(pay.amount) }}</span>
                  @if (!pay.isVoided) {
                    <button type="button" class="btn-ghost px-2 py-1 text-xs hover:text-danger" (click)="voidPayment(p, pay)" [attr.aria-label]="'Anular el pago de ' + money(pay.amount)">Anular</button>
                  }
                </li>
              }
            </ul>
          }
          @if (p.notes) { <p class="mt-2 text-xs text-muted">{{ p.notes }}</p> }
          <p class="mt-2 text-xs text-faint">Registrada por {{ p.createdByName }}</p>
        </div>
        <div class="space-y-3 border-t border-line px-6 py-4">
          @if (voiding()) {
            <label for="purchase-void-reason" class="field-label">Motivo de la anulación</label>
            <input id="purchase-void-reason" class="field-input" maxlength="200" [(ngModel)]="reason" />
            @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
            <p class="text-xs text-faint">Las unidades salen del inventario. El costo promedio no se recalcula.</p>
            <div class="flex justify-end gap-2">
              <button type="button" class="btn-secondary" (click)="voiding.set(false)" [disabled]="saving()">Cancelar</button>
              <button type="button" class="btn-danger" (click)="confirmVoid(p)" [disabled]="saving() || !reason.trim()">{{ saving() ? 'Anulando…' : 'Anular compra' }}</button>
            </div>
          } @else {
            <div class="flex justify-end gap-2">
              @if (p.status === 'Completed' && p.paidAmount === 0) {
                <button type="button" class="btn-ghost mr-auto hover:text-danger" (click)="voiding.set(true)">Anular</button>
              } @else if (p.status === 'Completed') {
                <p class="mr-auto self-center text-xs text-faint">Para anularla, anula primero sus pagos.</p>
              }
              @if (p.status === 'Completed' && p.balance > 0) {
                <button type="button" class="btn-secondary" (click)="pay(p)">Registrar pago</button>
              }
              <button type="button" class="btn-primary" (click)="ref.close(changed())">Listo</button>
            </div>
          }
        </div>
      } @else {
        <div class="m-6 h-48 animate-pulse rounded-xl bg-surface-2"></div>
      }
    </div>
  `,
})
export class PurchaseDetailDialog {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly id = inject<string>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly money = formatPrice;
  protected readonly date = formatIsoDate;

  private readonly loaded = httpResource<Purchase>(() => `${API_BASE}/purchases/${this.id}`);
  private readonly updated = signal<Purchase | null>(null);
  protected readonly purchase = computed(() => this.updated() ?? (this.loaded.hasValue() ? this.loaded.value() : null));

  protected readonly voiding = signal(false);
  protected readonly changed = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected reason = '';

  protected confirmVoid(p: Purchase): void {
    this.saving.set(true);
    this.error.set(null);
    this.http.post<Purchase>(`${API_BASE}/purchases/${p.id}/void`, { reason: this.reason.trim() }, { context: silentErrors() }).subscribe({
      next: (purchase) => {
        this.updated.set(purchase);
        this.changed.set(true);
        this.voiding.set(false);
        this.saving.set(false);
      },
      error: (e: unknown) => {
        this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }

  protected methodLabel(method: PaymentMethod): string {
    return PAYMENT_METHODS.find((m) => m.value === method)?.label ?? method;
  }

  protected pay(p: Purchase): void {
    const data: PaySupplierDialogData = { purchaseId: p.id, number: p.number, supplierName: p.supplierName, balance: p.balance };
    this.dialog
      .open<SupplierPayment>(PaySupplierDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((pay): pay is SupplierPayment => !!pay))
      .subscribe(() => this.refresh());
  }

  protected voidPayment(p: Purchase, payment: SupplierPayment): void {
    const data: VoidDialogData = {
      title: `Anular el pago de ${this.money(payment.amount)}`,
      hint: `El saldo de la compra ${p.number} vuelve a subir${payment.fromCashRegister ? ' y el efectivo vuelve a contar en la caja' : ''}.`,
      url: `${API_BASE}/supplier-payments/${payment.id}/void`,
      confirmText: 'Anular pago',
    };
    this.dialog
      .open<SupplierPayment>(VoidDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((pay): pay is SupplierPayment => !!pay))
      .subscribe(() => this.refresh());
  }

  /** vuelve a pedir la compra para ver el saldo y los pagos al dia */
  private refresh(): void {
    this.changed.set(true);
    this.http.get<Purchase>(`${API_BASE}/purchases/${this.id}`).subscribe((purchase) => this.updated.set(purchase));
  }
}
