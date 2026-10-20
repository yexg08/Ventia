import { Dialog } from '@angular/cdk/dialog';
import { HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroDocumentText } from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE } from '../../../core/http/api';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { localIsoDate as today } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import { ORDER_TYPES, OrderType, SaleStatus, SaleSummary } from '../business.models';
import { ReceiptDialog, ReceiptDialogData } from './receipt-dialog';


@Component({
  selector: 'app-sales-page',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroDocumentText })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Ventas</h1>
      <p class="mt-1 text-muted">{{ auth.isOwner() ? 'Todas las ventas del negocio.' : 'Las ventas que has registrado.' }} Toca una para ver su comprobante.</p>

      <div class="mt-8 flex flex-wrap items-end gap-3">
        <div>
          <label for="sales-from" class="field-label">Desde</label>
          <input id="sales-from" type="date" class="field-input" [ngModel]="from()" (ngModelChange)="from.set($event)" [max]="to()" />
        </div>
        <div>
          <label for="sales-to" class="field-label">Hasta</label>
          <input id="sales-to" type="date" class="field-input" [ngModel]="to()" (ngModelChange)="to.set($event)" [min]="from()" />
        </div>
        <div>
          <label for="sales-status" class="field-label">Estado</label>
          <select id="sales-status" class="field-input" [ngModel]="status()" (ngModelChange)="status.set($event)">
            <option value="">Todas</option>
            <option value="Completed">Completadas</option>
            <option value="Voided">Anuladas</option>
          </select>
        </div>
      </div>

      @if (sales.isLoading() && sales.value().length === 0) {
        <div class="card mt-4 h-48 animate-pulse bg-surface-2" aria-label="Cargando ventas"></div>
      } @else if (sales.error()) {
        <div class="card mt-4 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar las ventas.</p>
          <button type="button" class="btn-secondary mt-4" (click)="sales.reload()">Reintentar</button>
        </div>
      } @else if (sales.value().length === 0) {
        <div class="card mt-4 border-dashed p-10 text-center">
          <ng-icon name="heroDocumentText" size="32" class="text-faint" />
          <p class="mt-3 font-semibold">No hay ventas en esas fechas.</p>
        </div>
      } @else {
        <p class="mt-4 text-sm text-muted">
          {{ completedCount() }} venta(s) por <strong class="text-ink tabular-nums">{{ money(completedTotal()) }}</strong>
          @if (voidedCount()) { · {{ voidedCount() }} anulada(s) }
        </p>
        <section class="card mt-2 overflow-hidden" aria-label="Ventas">
          <div class="overflow-x-auto">
            <table class="w-full min-w-[40rem] text-left text-sm">
              <thead class="border-b border-line bg-surface-2 text-xs tracking-wider text-muted uppercase">
                <tr>
                  <th scope="col" class="px-5 py-3 font-semibold">N.°</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Hora</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Pedido</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Cliente</th>
                  @if (auth.isOwner()) { <th scope="col" class="px-5 py-3 font-semibold">Atendió</th> }
                  <th scope="col" class="px-5 py-3 text-right font-semibold">Total</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-line">
                @for (s of sales.value(); track s.id) {
                  <tr class="cursor-pointer transition hover:bg-surface-2" [class.opacity-60]="s.status === 'Voided'" (click)="open(s)">
                    <td class="px-5 py-3 font-semibold tabular-nums">
                      <button type="button" class="hover:underline" (click)="$event.stopPropagation(); open(s)" [attr.aria-label]="'Ver comprobante de la venta ' + s.number">{{ s.number }}</button>
                    </td>
                    <td class="px-5 py-3 whitespace-nowrap text-muted">{{ time(s.createdAt) }}</td>
                    <td class="px-5 py-3">{{ orderLabel(s.orderType) }} · {{ s.itemCount }} ítem(s)</td>
                    <td class="px-5 py-3">{{ s.customerName }}</td>
                    @if (auth.isOwner()) { <td class="px-5 py-3 text-muted">{{ s.sellerName }}</td> }
                    <td class="px-5 py-3 text-right whitespace-nowrap">
                      @if (s.status === 'Voided') {
                        <span class="mr-2 rounded-full bg-danger/15 px-2 py-0.5 text-xs font-semibold text-danger">Anulada</span>
                      }
                      <span class="font-semibold tabular-nums" [class.line-through]="s.status === 'Voided'">{{ money(s.total) }}</span>
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
export class SalesPage {
  protected readonly auth = inject(AuthService);
  private readonly dialog = inject(Dialog);
  protected readonly money = formatPrice;

  protected readonly from = signal(today());
  protected readonly to = signal(today());
  protected readonly status = signal<SaleStatus | ''>('');

  protected readonly sales = httpResource<SaleSummary[]>(
    () => {
      let params = new HttpParams().set('from', this.from()).set('to', this.to());
      if (this.status()) params = params.set('status', this.status());
      return { url: `${API_BASE}/sales`, params };
    },
    { defaultValue: [] },
  );

  protected readonly completed = computed(() => this.sales.value().filter((s) => s.status === 'Completed'));
  protected readonly completedCount = computed(() => this.completed().length);
  protected readonly completedTotal = computed(() => this.completed().reduce((sum, s) => sum + s.total, 0));
  protected readonly voidedCount = computed(() => this.sales.value().filter((s) => s.status === 'Voided').length);

  protected orderLabel(type: OrderType): string {
    return ORDER_TYPES.find((t) => t.value === type)?.label ?? type;
  }

  protected time(iso: string): string {
    const multiDay = this.from() !== this.to();
    return new Intl.DateTimeFormat('es-CO', multiDay ? { dateStyle: 'short', timeStyle: 'short' } : { timeStyle: 'short' }).format(new Date(iso));
  }

  protected open(sale: SaleSummary): void {
    const data: ReceiptDialogData = { sale: null, saleId: sale.id };
    this.dialog
      .open<boolean>(ReceiptDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter(Boolean))
      .subscribe(() => this.sales.reload());
  }
}
