import { Dialog, DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroPlus, heroPrinter, heroTrash, heroUser, heroXMark } from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { cartTotals } from '../../../shared/cart';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { addDays, formatIsoDate, localIsoDate } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import { Customer, Product, Quote, QuoteUpsert, Sale } from '../business.models';
import { PaymentDialog, PaymentDialogData } from '../pos/payment-dialog';
import { CustomerPicker } from '../pos/pos-dialogs';
import { ReceiptDialog, ReceiptDialogData } from '../pos/receipt-dialog';

interface DraftLine {
  key: string;
  productId: string;
  quantity: number;
  discountPercent: number;
}

/** crear o editar una cotizacion (al editar toma los precios de hoy) y cierra con la cotizacion guardada */
@Component({
  selector: 'app-quote-editor-dialog',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroPlus, heroTrash, heroUser, heroXMark })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="save()" class="card flex max-h-[calc(100dvh-2rem)] w-[min(44rem,calc(100vw-2rem))] flex-col !shadow-2xl" aria-labelledby="quote-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="quote-title" class="text-lg font-bold">{{ quote ? 'Editar cotización N.° ' + quote.number : 'Nueva cotización' }}</h2>
        @if (quote) {
          <p class="text-sm text-muted">Al guardar se toman los precios de hoy.</p>
        }
      </div>

      <div class="space-y-5 overflow-y-auto px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }

        <div class="grid gap-4 sm:grid-cols-[1fr_12rem]">
          <div>
            <label for="quote-customer" class="field-label">A nombre de</label>
            @if (customer(); as c) {
              <div class="flex items-center gap-2 rounded-xl border border-line px-3 py-2">
                <ng-icon name="heroUser" size="17" class="text-faint" />
                <span class="flex-1 truncate text-sm font-semibold">{{ c.name }}</span>
                <button type="button" class="btn-ghost px-1.5 py-1" (click)="customer.set(null)" aria-label="Quitar cliente"><ng-icon name="heroXMark" size="16" /></button>
              </div>
            } @else {
              <div class="flex gap-2">
                <input id="quote-customer" class="field-input" maxlength="100" placeholder="Ej. Evento de Juan Pérez" [(ngModel)]="customerName" name="customerName" [attr.aria-invalid]="!!fieldErrors()['customerName']" />
                <button type="button" class="btn-secondary shrink-0" (click)="pickCustomer()">Cliente</button>
              </div>
            }
            @if (fieldErrors()['customerName']) { <p class="field-error">{{ fieldErrors()['customerName'] }}</p> }
          </div>
          <div>
            <label for="quote-valid" class="field-label">Válida hasta</label>
            <input id="quote-valid" type="date" class="field-input" [min]="today" [max]="maxDate" [(ngModel)]="validUntil" name="validUntil" required [attr.aria-invalid]="!!fieldErrors()['validUntil']" />
            @if (fieldErrors()['validUntil']) { <p class="field-error">{{ fieldErrors()['validUntil'] }}</p> }
          </div>
        </div>

        <fieldset>
          <legend class="field-label">Productos</legend>
          <div class="space-y-2">
            @for (line of lines(); track line.key; let i = $index) {
              <div class="grid grid-cols-[1fr_4.5rem_4.5rem_6.5rem_auto] items-center gap-2">
                <label class="sr-only" [for]="'qp-' + line.key">Producto {{ i + 1 }}</label>
                <select [id]="'qp-' + line.key" class="field-input" [ngModel]="line.productId" [name]="'qp-' + line.key" (ngModelChange)="patch(line, { productId: $event })">
                  <option value="" disabled>Producto</option>
                  @for (p of activeProducts(); track p.id) {
                    <option [value]="p.id">{{ p.name }} · {{ money(p.price) }}</option>
                  }
                </select>
                <label class="sr-only" [for]="'qq-' + line.key">Cantidad</label>
                <input [id]="'qq-' + line.key" type="number" min="1" step="1" class="field-input tabular-nums" [ngModel]="line.quantity" [name]="'qq-' + line.key" (ngModelChange)="patch(line, { quantity: num($event) })" />
                <label class="sr-only" [for]="'qd-' + line.key">Descuento en porcentaje</label>
                <input [id]="'qd-' + line.key" type="number" min="0" max="100" class="field-input tabular-nums" [ngModel]="line.discountPercent" [name]="'qd-' + line.key" (ngModelChange)="patch(line, { discountPercent: num($event) })" />
                <p class="text-right text-sm font-semibold tabular-nums">{{ money(lineTotal(line)) }}</p>
                <button type="button" class="btn-ghost px-2.5" (click)="removeLine(line)" [disabled]="lines().length === 1" [attr.aria-label]="'Quitar línea ' + (i + 1)"><ng-icon name="heroTrash" size="17" /></button>
              </div>
            }
          </div>
          <p class="mt-1 grid grid-cols-[1fr_4.5rem_4.5rem_6.5rem_auto] gap-2 text-xs text-faint" aria-hidden="true"><span></span><span>Cant.</span><span>Desc. %</span><span></span><span class="w-10"></span></p>
          <button type="button" class="btn-ghost mt-1 px-2 text-sm" (click)="addLine()"><ng-icon name="heroPlus" size="16" /> Otro producto</button>
        </fieldset>

        <div>
          <label for="quote-notes" class="field-label">Notas <span class="font-normal text-faint">(opcional)</span></label>
          <textarea id="quote-notes" rows="2" class="field-input resize-none" maxlength="500" placeholder="Fecha del evento, condiciones…" [(ngModel)]="notes" name="notes"></textarea>
        </div>
      </div>

      <div class="flex items-center justify-between gap-2 border-t border-line px-6 py-4">
        <p class="text-lg font-extrabold tabular-nums">Total {{ money(totals().total) }}</p>
        <div class="flex gap-2">
          <button type="button" class="btn-secondary" (click)="ref.close()">Cancelar</button>
          <button type="submit" class="btn-primary" [disabled]="saving() || !valid()">{{ saving() ? 'Guardando…' : 'Guardar' }}</button>
        </div>
      </div>
    </form>
  `,
})
export class QuoteEditorDialog {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  protected readonly quote = inject<Quote | null>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Quote>>(DialogRef);
  protected readonly money = formatPrice;
  protected readonly today = localIsoDate();
  protected readonly maxDate = addDays(this.today, 90);

  private readonly products = httpResource<Product[]>(() => `${API_BASE}/products`, { defaultValue: [] });
  protected readonly activeProducts = computed(() => this.products.value().filter((p) => p.isActive));

  protected readonly customer = signal<Pick<Customer, 'id' | 'name'> | null>(
    this.quote?.customerId ? { id: this.quote.customerId, name: this.quote.customerName } : null,
  );
  protected customerName = this.quote && !this.quote.customerId ? this.quote.customerName : '';
  protected validUntil = this.quote && this.quote.validUntil >= this.today ? this.quote.validUntil : addDays(this.today, 15);
  protected notes = this.quote?.notes ?? '';
  protected readonly lines = signal<DraftLine[]>(
    this.quote?.lines.map((l) => ({ key: crypto.randomUUID(), productId: l.productId, quantity: l.quantity, discountPercent: l.discountPercent })) ?? [
      { key: crypto.randomUUID(), productId: '', quantity: 1, discountPercent: 0 },
    ],
  );

  /** totales con los precios de hoy (los mismos que tomara el servidor al guardar) */
  protected readonly totals = computed(() =>
    cartTotals(
      this.lines().flatMap((l) => {
        const p = this.products.value().find((x) => x.id === l.productId);
        return p ? [{ unitPrice: p.price, quantity: l.quantity, discountPercent: l.discountPercent, tax: p.tax }] : [];
      }),
    ),
  );
  protected readonly valid = computed(() =>
    this.lines().every((l) => l.productId && l.quantity >= 1 && Number.isInteger(l.quantity) && l.discountPercent >= 0 && l.discountPercent <= 100),
  );

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});

  protected num(value: unknown): number {
    const n = Number(value);
    return Number.isFinite(n) && n >= 0 ? n : 0;
  }

  protected lineTotal(line: DraftLine): number {
    const p = this.products.value().find((x) => x.id === line.productId);
    if (!p) return 0;
    return cartTotals([{ unitPrice: p.price, quantity: line.quantity, discountPercent: line.discountPercent, tax: p.tax }]).total;
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, { key: crypto.randomUUID(), productId: '', quantity: 1, discountPercent: 0 }]);
  }

  protected removeLine(line: DraftLine): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== line.key));
  }

  protected patch(line: DraftLine, changes: Partial<DraftLine>): void {
    this.lines.update((lines) => lines.map((l) => (l.key === line.key ? { ...l, ...changes } : l)));
  }

  protected pickCustomer(): void {
    this.dialog
      .open<Customer>(CustomerPicker, DIALOG_DEFAULTS)
      .closed.pipe(filter((c): c is Customer => !!c))
      .subscribe((c) => this.customer.set(c));
  }

  protected save(): void {
    if (!this.valid()) return;
    const body: QuoteUpsert = {
      customerId: this.customer()?.id ?? null,
      customerName: this.customer() ? null : this.customerName.trim() || null,
      validUntil: this.validUntil,
      notes: this.notes.trim() || null,
      lines: this.lines().map((l) => ({ productId: l.productId, quantity: l.quantity, discountPercent: l.discountPercent })),
    };
    const request = this.quote
      ? this.http.put<Quote>(`${API_BASE}/quotes/${this.quote.id}`, body, { context: silentErrors() })
      : this.http.post<Quote>(`${API_BASE}/quotes`, body, { context: silentErrors() });
    this.saving.set(true);
    this.error.set(null);
    request.subscribe({
      next: (quote) => this.ref.close(quote),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.fieldErrors.set(fields);
        this.error.set(Object.keys(fields).length ? null : getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}

export type QuoteAction = 'edit' | 'changed';

/** cotizacion imprimible con editar y cancelar y convertir en venta (cierra con lo que hay que hacer despues) */
@Component({
  selector: 'app-quote-detail-dialog',
  imports: [NgIcon],
  providers: [provideIcons({ heroPrinter })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card flex max-h-[calc(100dvh-2rem)] w-[min(28rem,calc(100vw-2rem))] flex-col !shadow-2xl print:max-h-none print:w-full print:border-0 print:shadow-none" role="dialog" aria-label="Cotización">
      @if (quote(); as q) {
        <div class="overflow-y-auto px-6 py-5 text-sm print:overflow-visible">
          <div class="text-center">
            <p class="text-base font-bold">{{ q.business.name }}</p>
            @if (q.business.taxId) { <p class="text-xs text-muted">NIT {{ q.business.taxId }}</p> }
            @if (q.business.phone) { <p class="text-xs text-muted">Tel. {{ q.business.phone }}</p> }
            <p class="mt-3 text-xs font-bold tracking-wider uppercase">Cotización N.° {{ q.number }}</p>
            <p class="text-xs text-faint">No es una factura</p>
          </div>

          @if (q.status === 'Converted') {
            <p class="mt-4 rounded-xl border border-line bg-surface-2 px-3 py-2 text-center text-xs">Convertida en la venta N.° {{ q.saleNumber }}</p>
          } @else if (q.status === 'Cancelled') {
            <p class="mt-4 rounded-xl border border-danger/40 bg-danger/10 px-3 py-2 text-center text-xs text-danger">Cancelada</p>
          } @else if (q.isExpired) {
            <p class="mt-4 rounded-xl border border-danger/40 bg-danger/10 px-3 py-2 text-center text-xs text-danger">Vencida: edítala para tomar los precios de hoy.</p>
          }

          <dl class="mt-4 grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-xs">
            <dt class="text-muted">Para</dt><dd>{{ q.customerName }}</dd>
            <dt class="text-muted">Fecha</dt><dd>{{ dateTime(q.createdAt) }}</dd>
            <dt class="text-muted">Válida hasta</dt><dd>{{ date(q.validUntil) }}</dd>
            <dt class="text-muted">Elaboró</dt><dd>{{ q.createdByName }}</dd>
          </dl>

          <table class="mt-4 w-full text-xs">
            <thead class="border-y border-line text-muted">
              <tr><th scope="col" class="py-1.5 text-left font-semibold">Cant.</th><th scope="col" class="py-1.5 text-left font-semibold">Producto</th><th scope="col" class="py-1.5 text-right font-semibold">Total</th></tr>
            </thead>
            <tbody>
              @for (l of q.lines; track l.id) {
                <tr class="align-top">
                  <td class="py-1 tabular-nums">{{ l.quantity }}</td>
                  <td class="py-1">{{ l.productName }} <span class="text-faint">× {{ money(l.unitPrice) }}</span>
                    @if (l.discountAmount > 0) { <span class="block text-faint">Descuento {{ l.discountPercent }} %</span> }
                  </td>
                  <td class="py-1 text-right tabular-nums">{{ money(l.lineTotal) }}</td>
                </tr>
              }
            </tbody>
          </table>
          <dl class="mt-3 space-y-0.5 border-t border-line pt-3 text-xs">
            <div class="flex justify-between"><dt class="text-muted">Subtotal (sin impuestos)</dt><dd class="tabular-nums">{{ money(q.subtotal) }}</dd></div>
            <div class="flex justify-between"><dt class="text-muted">Impuestos</dt><dd class="tabular-nums">{{ money(q.taxTotal) }}</dd></div>
            <div class="flex justify-between pt-1 text-base font-bold"><dt>Total</dt><dd class="tabular-nums">{{ money(q.total) }}</dd></div>
          </dl>
          @if (q.notes) { <p class="mt-3 text-xs">{{ q.notes }}</p> }
        </div>

        <div class="space-y-2 border-t border-line px-6 py-4 print:hidden">
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <div class="flex flex-wrap justify-end gap-2">
            @if (q.status === 'Open') {
              <button type="button" class="btn-ghost mr-auto hover:text-danger" (click)="cancel(q)" [disabled]="busy()">Cancelar cotización</button>
              <button type="button" class="btn-secondary" (click)="ref.close('edit')">Editar</button>
            }
            <button type="button" class="btn-secondary" (click)="print()"><ng-icon name="heroPrinter" size="17" /> Imprimir</button>
            @if (q.status === 'Open' && !q.isExpired) {
              <button type="button" class="btn-primary" (click)="convert(q)" [disabled]="busy()">Convertir en venta</button>
            } @else {
              <button type="button" class="btn-primary" (click)="ref.close(changed() ? 'changed' : undefined)">Listo</button>
            }
          </div>
        </div>
      } @else {
        <div class="m-6 h-64 skeleton rounded-xl"></div>
      }
    </div>
  `,
})
export class QuoteDetailDialog {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly id = inject<string>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<QuoteAction>>(DialogRef);
  protected readonly money = formatPrice;
  protected readonly date = formatIsoDate;

  protected readonly loaded = httpResource<Quote>(() => `${API_BASE}/quotes/${this.id}`);
  private readonly updated = signal<Quote | null>(null);
  protected readonly quote = computed(() => this.updated() ?? (this.loaded.hasValue() ? this.loaded.value() : null));
  protected readonly changed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected dateTime(iso: string): string {
    return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso));
  }

  protected print(): void {
    window.print();
  }

  protected cancel(q: Quote): void {
    this.busy.set(true);
    this.error.set(null);
    this.http.post<Quote>(`${API_BASE}/quotes/${q.id}/cancel`, null, { context: silentErrors() }).subscribe({
      next: (quote) => {
        this.updated.set(quote);
        this.changed.set(true);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(getErrorMessage(e));
        this.busy.set(false);
      },
    });
  }

  /** cobra con los precios cotizados (necesita la caja abierta) y muestra el comprobante */
  protected convert(q: Quote): void {
    const data: PaymentDialogData = { total: q.total, url: `${API_BASE}/quotes/${q.id}/convert`, request: { orderType: 'TakeAway' } };
    this.dialog
      .open<Sale>(PaymentDialog, { data, ...DIALOG_DEFAULTS, disableClose: true })
      .closed.pipe(filter((sale): sale is Sale => !!sale))
      .subscribe((sale) => {
        this.ref.close('changed');
        const receipt: ReceiptDialogData = { sale, saleId: sale.id };
        this.dialog.open(ReceiptDialog, { data: receipt, ...DIALOG_DEFAULTS });
      });
  }
}
