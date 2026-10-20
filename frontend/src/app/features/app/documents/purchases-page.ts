import { Dialog } from '@angular/cdk/dialog';
import { HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroInboxArrowDown, heroPlus } from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { ToastService } from '../../../core/notifications/toast.service';
import { API_BASE } from '../../../core/http/api';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { addDays, formatIsoDate, localIsoDate } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import { Purchase, PurchaseSummary } from '../business.models';
import { NewPurchaseDialog, PurchaseDetailDialog } from './purchase-dialogs';

@Component({
  selector: 'app-purchases-page',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroInboxArrowDown, heroPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Compras</h1>
          <p class="mt-1 text-muted">Lo que le compras a tus proveedores. Cada compra sube el stock.</p>
        </div>
        <button type="button" class="btn-primary" (click)="create()"><ng-icon name="heroPlus" size="18" /> Nueva compra</button>
      </div>

      <div class="mt-8 flex flex-wrap items-end gap-3">
        <div>
          <label for="purchases-from" class="field-label">Desde</label>
          <input id="purchases-from" type="date" class="field-input" [ngModel]="from()" (ngModelChange)="from.set($event)" [max]="to()" />
        </div>
        <div>
          <label for="purchases-to" class="field-label">Hasta</label>
          <input id="purchases-to" type="date" class="field-input" [ngModel]="to()" (ngModelChange)="to.set($event)" [min]="from()" />
        </div>
      </div>

      @if (purchases.isLoading() && purchases.value().length === 0) {
        <div class="card mt-4 h-48 animate-pulse bg-surface-2" aria-label="Cargando compras"></div>
      } @else if (purchases.error()) {
        <div class="card mt-4 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar las compras.</p>
          <button type="button" class="btn-secondary mt-4" (click)="purchases.reload()">Reintentar</button>
        </div>
      } @else if (purchases.value().length === 0) {
        <div class="card mt-4 border-dashed p-10 text-center">
          <ng-icon name="heroInboxArrowDown" size="32" class="text-faint" />
          <p class="mt-3 font-semibold">No hay compras en esas fechas.</p>
        </div>
      } @else {
        <p class="mt-4 text-sm text-muted">Total comprado: <strong class="text-ink tabular-nums">{{ money(total()) }}</strong> (sin anuladas)</p>
        <section class="card mt-2 overflow-hidden" aria-label="Compras">
          <div class="overflow-x-auto">
            <table class="w-full min-w-[36rem] text-left text-sm">
              <thead class="border-b border-line bg-surface-2 text-xs tracking-wider text-muted uppercase">
                <tr>
                  <th scope="col" class="px-5 py-3 font-semibold">N.°</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Fecha</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Proveedor</th>
                  <th scope="col" class="px-5 py-3 text-right font-semibold">Unidades</th>
                  <th scope="col" class="px-5 py-3 text-right font-semibold">Total</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-line">
                @for (p of purchases.value(); track p.id) {
                  <tr class="cursor-pointer transition hover:bg-surface-2" [class.opacity-60]="p.status === 'Voided'" (click)="open(p)">
                    <td class="px-5 py-3 font-semibold tabular-nums">
                      <button type="button" class="hover:underline" (click)="$event.stopPropagation(); open(p)" [attr.aria-label]="'Ver la compra ' + p.number">{{ p.number }}</button>
                    </td>
                    <td class="px-5 py-3 whitespace-nowrap text-muted">{{ date(p.date) }}</td>
                    <td class="px-5 py-3">{{ p.supplierName }}@if (p.supplierInvoice) { <span class="text-xs text-faint"> · {{ p.supplierInvoice }}</span> }</td>
                    <td class="px-5 py-3 text-right tabular-nums">{{ p.itemCount }}</td>
                    <td class="px-5 py-3 text-right whitespace-nowrap">
                      @if (p.status === 'Voided') { <span class="mr-2 rounded-full bg-danger/15 px-2 py-0.5 text-xs font-semibold text-danger">Anulada</span> }
                      <span class="font-semibold tabular-nums" [class.line-through]="p.status === 'Voided'">{{ money(p.total) }}</span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>
  `,
})
export class PurchasesPage {
  private readonly dialog = inject(Dialog);
  private readonly toasts = inject(ToastService);
  protected readonly money = formatPrice;
  protected readonly date = formatIsoDate;

  protected readonly to = signal(localIsoDate());
  protected readonly from = signal(addDays(localIsoDate(), -30));
  protected readonly purchases = httpResource<PurchaseSummary[]>(
    () => ({ url: `${API_BASE}/purchases`, params: new HttpParams().set('from', this.from()).set('to', this.to()) }),
    { defaultValue: [] },
  );
  protected readonly total = computed(() =>
    this.purchases.value().filter((p) => p.status === 'Completed').reduce((sum, p) => sum + p.total, 0),
  );

  protected create(): void {
    this.dialog
      .open<Purchase>(NewPurchaseDialog, DIALOG_DEFAULTS)
      .closed.pipe(filter((p): p is Purchase => !!p))
      .subscribe((p) => {
        this.toasts.success(`Compra ${p.number} registrada. El stock ya se actualizó.`);
        this.purchases.reload();
      });
  }

  protected open(purchase: PurchaseSummary): void {
    this.dialog
      .open<boolean>(PurchaseDetailDialog, { data: purchase.id, ...DIALOG_DEFAULTS })
      .closed.pipe(filter(Boolean))
      .subscribe(() => this.purchases.reload());
  }
}
