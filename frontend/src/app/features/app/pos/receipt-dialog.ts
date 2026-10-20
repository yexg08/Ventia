import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroPrinter } from '@ng-icons/heroicons/outline';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE, getErrorMessage, silentErrors } from '../../../core/http/api';
import { formatPrice } from '../../../shared/format';
import { ORDER_TYPES, PAYMENT_METHODS, PaymentMethod, Sale, SaleLine } from '../business.models';

export interface ReceiptDialogData {
  /** venta ya cargada (recien cobrada) o solo su id */
  sale: Sale | null;
  saleId: string;
}

/** comprobante de una venta (se puede imprimir y el dueno puede anularla) y cierra con true si se anulo */
@Component({
  selector: 'app-receipt-dialog',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroPrinter })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card flex max-h-[calc(100dvh-2rem)] w-[min(26rem,calc(100vw-2rem))] flex-col !shadow-2xl print:max-h-none print:w-full print:border-0 print:shadow-none" role="dialog" aria-label="Comprobante de venta">
      @if (sale(); as s) {
        <div class="overflow-y-auto px-6 py-5 text-sm print:overflow-visible">
          <div class="text-center">
            <p class="text-base font-bold">{{ s.business.name }}</p>
            @if (s.business.taxId) { <p class="text-xs text-muted">NIT {{ s.business.taxId }}</p> }
            @if (s.business.address) { <p class="text-xs text-muted">{{ s.business.address }}{{ s.business.city ? ', ' + s.business.city : '' }}</p> }
            @if (s.business.phone) { <p class="text-xs text-muted">Tel. {{ s.business.phone }}</p> }
            <p class="mt-3 text-xs font-bold tracking-wider uppercase">Comprobante de venta N.° {{ s.number }}</p>
            <p class="text-xs font-semibold text-danger">Sin validez fiscal (demo)</p>
          </div>

          @if (s.status === 'Voided') {
            <p class="mt-4 rounded-xl border border-danger/40 bg-danger/10 px-3 py-2 text-center text-xs text-danger">
              <strong>ANULADA</strong> por {{ s.voidedBy }} el {{ dateTime(s.voidedAt) }}: {{ s.voidReason }}
            </p>
          }

          <dl class="mt-4 grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-xs">
            <dt class="text-muted">Fecha</dt><dd>{{ dateTime(s.createdAt) }}</dd>
            <dt class="text-muted">Atendió</dt><dd>{{ s.sellerName }}</dd>
            <dt class="text-muted">Pedido</dt><dd>{{ orderLabel(s) }}</dd>
            <dt class="text-muted">Cliente</dt><dd>{{ s.customerName }}{{ s.customerDocument ? ' · ' + s.customerDocument : '' }}</dd>
          </dl>

          <table class="mt-4 w-full text-xs">
            <thead class="border-y border-line text-muted">
              <tr>
                <th scope="col" class="py-1.5 text-left font-semibold">Cant.</th>
                <th scope="col" class="py-1.5 text-left font-semibold">Producto</th>
                <th scope="col" class="py-1.5 text-right font-semibold">Total</th>
              </tr>
            </thead>
            <tbody>
              @for (line of s.lines; track line.id) {
                <tr class="align-top">
                  <td class="py-1 tabular-nums">{{ line.quantity }}</td>
                  <td class="py-1" [class.pl-3]="line.parentLineId">
                    {{ line.parentLineId ? '+ ' : '' }}{{ line.productName }}
                    @if (line.discountAmount > 0) { <span class="block text-faint">Descuento {{ line.discountPercent }} % (−{{ money(line.discountAmount) }})</span> }
                    @if (line.note) { <span class="block text-faint italic">{{ line.note }}</span> }
                  </td>
                  <td class="py-1 text-right tabular-nums">{{ money(line.lineTotal) }}</td>
                </tr>
              }
            </tbody>
          </table>

          <dl class="mt-3 space-y-0.5 border-t border-line pt-3 text-xs">
            <div class="flex justify-between"><dt class="text-muted">Subtotal (sin impuestos)</dt><dd class="tabular-nums">{{ money(s.subtotal) }}</dd></div>
            @for (tax of taxes(); track tax.label) {
              <div class="flex justify-between"><dt class="text-muted">{{ tax.label }}</dt><dd class="tabular-nums">{{ money(tax.amount) }}</dd></div>
            }
            @if (s.discountTotal > 0) {
              <div class="flex justify-between"><dt class="text-muted">Descuentos incluidos</dt><dd class="tabular-nums">−{{ money(s.discountTotal) }}</dd></div>
            }
            <div class="flex justify-between pt-1 text-base font-bold"><dt>Total</dt><dd class="tabular-nums">{{ money(s.total) }}</dd></div>
            @for (p of s.payments; track p.method) {
              <div class="flex justify-between"><dt class="text-muted">{{ methodLabel(p.method) }}</dt><dd class="tabular-nums">{{ money(p.amount) }}</dd></div>
            }
            @if (s.cashTendered > 0) {
              <div class="flex justify-between"><dt class="text-muted">Efectivo recibido</dt><dd class="tabular-nums">{{ money(s.cashTendered) }}</dd></div>
              <div class="flex justify-between font-semibold"><dt>Cambio</dt><dd class="tabular-nums">{{ money(s.change) }}</dd></div>
            }
          </dl>

          @if (s.business.footer) {
            <p class="mt-4 text-center text-xs">{{ s.business.footer }}</p>
          }
        </div>

        <div class="space-y-3 border-t border-line px-6 py-4 print:hidden">
          @if (voiding()) {
            <div>
              <label for="void-reason" class="field-label">Motivo de la anulación</label>
              <input id="void-reason" class="field-input" [(ngModel)]="voidReason" maxlength="200" />
              @if (error()) {
                <p class="field-error" role="alert">{{ error() }}</p>
              }
              <p class="mt-1.5 text-xs text-faint">El inventario descontado vuelve y la venta deja de contar en la caja y los reportes.</p>
            </div>
            <div class="flex justify-end gap-2">
              <button type="button" class="btn-secondary" (click)="voiding.set(false)" [disabled]="saving()">Cancelar</button>
              <button type="button" class="btn-danger" (click)="confirmVoid()" [disabled]="saving() || !voidReason.trim()">{{ saving() ? 'Anulando…' : 'Anular venta' }}</button>
            </div>
          } @else {
            <div class="flex flex-wrap justify-end gap-2">
              @if (auth.isOwner() && s.status === 'Completed') {
                <button type="button" class="btn-ghost mr-auto hover:text-danger" (click)="voiding.set(true)">Anular</button>
              }
              <button type="button" class="btn-secondary" (click)="print()"><ng-icon name="heroPrinter" size="17" /> Imprimir</button>
              <button type="button" class="btn-primary" (click)="ref.close(voided())">Listo</button>
            </div>
          }
        </div>
      } @else if (loaded.error()) {
        <p class="p-6 text-center text-sm text-muted">No se pudo cargar la venta.</p>
      } @else {
        <div class="m-6 h-64 skeleton rounded-xl"></div>
      }
    </div>
  `,
})
export class ReceiptDialog {
  private readonly http = inject(HttpClient);
  protected readonly auth = inject(AuthService);
  protected readonly data = inject<ReceiptDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly money = formatPrice;

  protected readonly loaded = httpResource<Sale>(() => (this.data.sale ? undefined : `${API_BASE}/sales/${this.data.saleId}`));
  private readonly updated = signal<Sale | null>(null);
  protected readonly sale = computed(() => this.updated() ?? this.data.sale ?? (this.loaded.hasValue() ? this.loaded.value() : null));

  protected readonly voiding = signal(false);
  protected readonly voided = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected voidReason = '';

  /** impuestos agrupados por tipo (INC 8 por ciento o IVA 19 por ciento) */
  protected readonly taxes = computed(() => {
    const groups = new Map<string, number>();
    for (const line of this.sale()?.lines ?? []) {
      if (line.lineTax === 0) continue;
      const label = taxLabel(line);
      groups.set(label, Math.round(((groups.get(label) ?? 0) + line.lineTax) * 100) / 100);
    }
    return [...groups].map(([label, amount]) => ({ label, amount }));
  });

  protected orderLabel(s: Sale): string {
    const type = ORDER_TYPES.find((t) => t.value === s.orderType)?.label ?? s.orderType;
    if (s.orderType === 'DineIn' && s.tableLabel) return `${type} ${s.tableLabel}`;
    if (s.orderType === 'Delivery' && s.deliveryAddress) return `${type} · ${s.deliveryAddress}`;
    return type;
  }

  protected methodLabel(method: PaymentMethod): string {
    return PAYMENT_METHODS.find((m) => m.value === method)?.label ?? method;
  }

  protected dateTime(iso: string | null): string {
    return iso ? new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso)) : '';
  }

  protected print(): void {
    window.print();
  }

  protected confirmVoid(): void {
    const s = this.sale();
    if (!s) return;
    this.saving.set(true);
    this.error.set(null);
    this.http.post<Sale>(`${API_BASE}/sales/${s.id}/void`, { reason: this.voidReason.trim() }, { context: silentErrors() }).subscribe({
      next: (sale) => {
        this.updated.set(sale);
        this.voided.set(true);
        this.voiding.set(false);
        this.saving.set(false);
      },
      error: (e: unknown) => {
        this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}

function taxLabel(line: SaleLine): string {
  switch (line.tax) {
    case 'Inc8':
      return 'INC 8 %';
    case 'Iva19':
      return 'IVA 19 %';
    case 'Iva5':
      return 'IVA 5 %';
    default:
      return 'Sin impuesto';
  }
}
