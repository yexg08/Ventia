import { Dialog } from '@angular/cdk/dialog';
import { HttpClient, HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroDocumentDuplicate, heroPlus } from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatIsoDate } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import { Quote, QuoteStatus, QuoteSummary } from '../business.models';
import { QuoteAction, QuoteDetailDialog, QuoteEditorDialog } from './quote-dialogs';

@Component({
  selector: 'app-quotes-page',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroDocumentDuplicate, heroPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Cotizaciones</h1>
          <p class="mt-1 text-muted">Para eventos y pedidos grandes. Mientras estén vigentes se venden con el precio cotizado.</p>
        </div>
        <button type="button" class="btn-primary" (click)="edit(null)"><ng-icon name="heroPlus" size="18" /> Nueva cotización</button>
      </div>

      <div class="mt-8">
        <label for="quotes-status" class="field-label">Estado</label>
        <select id="quotes-status" class="field-input w-auto" [ngModel]="status()" (ngModelChange)="status.set($event)">
          <option value="">Todas</option>
          <option value="Open">Abiertas</option>
          <option value="Converted">Convertidas en venta</option>
          <option value="Cancelled">Canceladas</option>
        </select>
      </div>

      @if (quotes.isLoading() && quotes.value().length === 0) {
        <div class="card mt-4 h-48 animate-pulse bg-surface-2" aria-label="Cargando cotizaciones"></div>
      } @else if (quotes.error()) {
        <div class="card mt-4 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar las cotizaciones.</p>
          <button type="button" class="btn-secondary mt-4" (click)="quotes.reload()">Reintentar</button>
        </div>
      } @else if (quotes.value().length === 0) {
        <div class="card mt-4 border-dashed p-10 text-center">
          <ng-icon name="heroDocumentDuplicate" size="32" class="text-faint" />
          <p class="mt-3 font-semibold">No hay cotizaciones.</p>
        </div>
      } @else {
        <section class="card mt-4 overflow-hidden" aria-label="Cotizaciones">
          <div class="overflow-x-auto">
            <table class="w-full min-w-[36rem] text-left text-sm">
              <thead class="border-b border-line bg-surface-2 text-xs tracking-wider text-muted uppercase">
                <tr>
                  <th scope="col" class="px-5 py-3 font-semibold">N.°</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Para</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Válida hasta</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Estado</th>
                  <th scope="col" class="px-5 py-3 text-right font-semibold">Total</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-line">
                @for (q of quotes.value(); track q.id) {
                  <tr class="cursor-pointer transition hover:bg-surface-2" (click)="open(q)">
                    <td class="px-5 py-3 font-semibold tabular-nums">
                      <button type="button" class="hover:underline" (click)="$event.stopPropagation(); open(q)" [attr.aria-label]="'Ver la cotización ' + q.number">{{ q.number }}</button>
                    </td>
                    <td class="px-5 py-3">{{ q.customerName }}</td>
                    <td class="px-5 py-3 whitespace-nowrap text-muted">{{ date(q.validUntil) }}</td>
                    <td class="px-5 py-3">
                      <span class="rounded-full px-2.5 py-1 text-xs font-semibold" [class]="badge(q)">{{ label(q) }}</span>
                    </td>
                    <td class="px-5 py-3 text-right font-semibold whitespace-nowrap tabular-nums">{{ money(q.total) }}</td>
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
export class QuotesPage {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly toasts = inject(ToastService);
  protected readonly money = formatPrice;
  protected readonly date = formatIsoDate;

  protected readonly status = signal<QuoteStatus | ''>('');
  protected readonly quotes = httpResource<QuoteSummary[]>(
    () => ({ url: `${API_BASE}/quotes`, params: this.status() ? new HttpParams().set('status', this.status()) : new HttpParams() }),
    { defaultValue: [] },
  );

  protected label(q: QuoteSummary): string {
    if (q.status === 'Converted') return 'Vendida';
    if (q.status === 'Cancelled') return 'Cancelada';
    return q.isExpired ? 'Vencida' : 'Abierta';
  }

  protected badge(q: QuoteSummary): string {
    if (q.status === 'Converted') return 'bg-success/15 text-success';
    if (q.status === 'Cancelled' || q.isExpired) return 'bg-danger/15 text-danger';
    return 'bg-surface-2 text-ink';
  }

  protected edit(quote: Quote | null): void {
    this.dialog
      .open<Quote>(QuoteEditorDialog, { data: quote, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((q): q is Quote => !!q))
      .subscribe((q) => {
        this.toasts.success(quote ? `Cotización ${q.number} actualizada.` : `Cotización ${q.number} creada.`);
        this.quotes.reload();
      });
  }

  protected open(summary: QuoteSummary): void {
    this.dialog
      .open<QuoteAction>(QuoteDetailDialog, { data: summary.id, ...DIALOG_DEFAULTS })
      .closed.subscribe((action) => {
        if (action === 'changed') this.quotes.reload();
        if (action === 'edit') this.http.get<Quote>(`${API_BASE}/quotes/${summary.id}`).subscribe((q) => this.edit(q));
      });
  }
}
