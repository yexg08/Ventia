import { Dialog } from '@angular/cdk/dialog';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroCheckCircle } from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatIsoDate } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import { Payable, SupplierPayment } from '../business.models';
import { PaySupplierDialog, PaySupplierDialogData } from '../expenses/expense-dialogs';
import { PurchaseDetailDialog } from './purchase-dialogs';

/** compras a credito con saldo pendiente (las mas antiguas primero) */
@Component({
  selector: 'app-payables-page',
  imports: [NgIcon],
  providers: [provideIcons({ heroCheckCircle })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Cuentas por pagar</h1>
      <p class="mt-1 text-muted">Compras a crédito que todavía le debes a tus proveedores.</p>

      @if (payables.isLoading() && payables.value().length === 0) {
        <div class="card mt-8 h-48 skeleton !bg-surface-2" aria-label="Cargando cuentas por pagar"></div>
      } @else if (payables.error()) {
        <div class="card mt-8 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar las cuentas por pagar.</p>
          <button type="button" class="btn-secondary mt-4" (click)="payables.reload()">Reintentar</button>
        </div>
      } @else if (payables.value().length === 0) {
        <div class="card mt-8 border-dashed p-10 text-center">
          <ng-icon name="heroCheckCircle" size="32" class="text-success" />
          <p class="mt-3 font-semibold">Estás al día con tus proveedores.</p>
          <p class="mt-1 text-sm text-muted">Las compras que registres a crédito aparecen aquí hasta que las pagues.</p>
        </div>
      } @else {
        <div class="mt-8 grid gap-3 sm:grid-cols-2">
          <div class="card p-5">
            <p class="text-sm text-muted">Total por pagar</p>
            <p class="mt-1 text-2xl font-extrabold tabular-nums">{{ money(totalOwed()) }}</p>
          </div>
          <div class="card p-5">
            <p class="text-sm text-muted">Con más de 30 días</p>
            <p class="mt-1 text-2xl font-extrabold tabular-nums" [class.text-danger]="overdue() > 0">{{ money(overdue()) }}</p>
          </div>
        </div>

        @for (group of bySupplier(); track group.supplierId) {
          <section class="card mt-4 overflow-hidden" [attr.aria-label]="'Deuda con ' + group.supplierName">
            <div class="flex items-center justify-between gap-3 border-b border-line bg-surface-2 px-5 py-3">
              <h2 class="font-bold">{{ group.supplierName }}</h2>
              <p class="text-sm font-semibold tabular-nums">{{ money(group.balance) }}</p>
            </div>
            <ul class="divide-y divide-line">
              @for (p of group.items; track p.purchaseId) {
                <li class="flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3 text-sm">
                  <button type="button" class="min-w-0 flex-1 text-left hover:underline" (click)="open(p)" [attr.aria-label]="'Ver la compra ' + p.number">
                    <span class="font-semibold">Compra N.° {{ p.number }}</span>
                    <span class="text-muted"> · {{ date(p.date) }}{{ p.supplierInvoice ? ' · factura ' + p.supplierInvoice : '' }}</span>
                  </button>
                  <span class="text-xs" [class]="p.daysOpen > 30 ? 'font-semibold text-danger' : 'text-faint'">{{ p.daysOpen === 0 ? 'hoy' : 'hace ' + p.daysOpen + (p.daysOpen === 1 ? ' día' : ' días') }}</span>
                  <span class="text-right tabular-nums">
                    @if (p.paidAmount > 0) { <span class="text-xs text-faint">abonado {{ money(p.paidAmount) }} · </span> }
                    <strong>{{ money(p.balance) }}</strong>
                  </span>
                  <button type="button" class="btn-secondary px-3 py-1.5 text-xs" (click)="pay(p)">Pagar</button>
                </li>
              }
            </ul>
          </section>
        }
      }
    </div>
  `,
})
export class PayablesPage {
  private readonly dialog = inject(Dialog);
  private readonly toasts = inject(ToastService);
  protected readonly money = formatPrice;
  protected readonly date = formatIsoDate;

  protected readonly payables = httpResource<Payable[]>(() => `${API_BASE}/payables`, { defaultValue: [] });
  protected readonly totalOwed = computed(() => this.payables.value().reduce((sum, p) => sum + p.balance, 0));
  protected readonly overdue = computed(() =>
    this.payables.value().filter((p) => p.daysOpen > 30).reduce((sum, p) => sum + p.balance, 0),
  );

  /** agrupadas por proveedor (el que mas se debe primero) */
  protected readonly bySupplier = computed(() => {
    const groups = new Map<string, { supplierId: string; supplierName: string; balance: number; items: Payable[] }>();
    for (const p of this.payables.value()) {
      const group = groups.get(p.supplierId) ?? { supplierId: p.supplierId, supplierName: p.supplierName, balance: 0, items: [] };
      group.balance += p.balance;
      group.items.push(p);
      groups.set(p.supplierId, group);
    }
    return [...groups.values()].sort((a, b) => b.balance - a.balance);
  });

  protected pay(payable: Payable): void {
    const data: PaySupplierDialogData = {
      purchaseId: payable.purchaseId,
      number: payable.number,
      supplierName: payable.supplierName,
      balance: payable.balance,
    };
    this.dialog
      .open<SupplierPayment>(PaySupplierDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((p): p is SupplierPayment => !!p))
      .subscribe((p) => {
        this.toasts.success(p.amount >= payable.balance ? `Compra ${payable.number} pagada.` : `Abono de ${this.money(p.amount)} registrado.`);
        this.payables.reload();
      });
  }

  protected open(payable: Payable): void {
    this.dialog
      .open<boolean>(PurchaseDetailDialog, { data: payable.purchaseId, ...DIALOG_DEFAULTS })
      .closed.pipe(filter(Boolean))
      .subscribe(() => this.payables.reload());
  }
}
