import { Dialog, DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient, HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroMagnifyingGlass, heroPlus } from '@ng-icons/heroicons/outline';
import { debounceTime, filter, map } from 'rxjs';
import { API_BASE, getErrorMessage, silentErrors } from '../../../core/http/api';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatPrice } from '../../../shared/format';
import { CashSession, Customer, PAYMENT_METHODS, PaymentMethod, Product } from '../business.models';
import { CustomerDialog } from '../parties/customer-dialog';

/** busca un cliente o crea uno nuevo (cierra con el cliente elegido) */
@Component({
  selector: 'app-customer-picker',
  imports: [ReactiveFormsModule, NgIcon],
  providers: [provideIcons({ heroMagnifyingGlass, heroPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card flex max-h-[calc(100dvh-2rem)] w-[min(30rem,calc(100vw-2rem))] flex-col shadow-2xl" role="dialog" aria-labelledby="picker-title">
      <div class="space-y-3 border-b border-line px-6 py-4">
        <h2 id="picker-title" class="text-lg font-bold">Elegir cliente</h2>
        <div class="relative">
          <ng-icon name="heroMagnifyingGlass" size="18" class="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-faint" />
          <label for="picker-search" class="sr-only">Buscar por nombre, documento o celular</label>
          <input id="picker-search" type="search" [formControl]="search" placeholder="Nombre, documento o celular" class="field-input pl-10" />
        </div>
      </div>
      <ul class="flex-1 divide-y divide-line overflow-y-auto">
        @for (c of customers.value(); track c.id) {
          <li>
            <button type="button" class="w-full px-6 py-3 text-left transition hover:bg-surface-2" (click)="ref.close(c)">
              <span class="block font-semibold">{{ c.name }}</span>
              <span class="block text-xs text-muted">{{ c.documentType }} {{ c.documentNumber }}{{ c.phone ? ' · ' + c.phone : '' }}</span>
            </button>
          </li>
        } @empty {
          <li class="px-6 py-8 text-center text-sm text-muted">{{ customers.isLoading() ? 'Buscando…' : 'No hay clientes con esa búsqueda.' }}</li>
        }
      </ul>
      <div class="flex justify-between gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="create()"><ng-icon name="heroPlus" size="17" /> Nuevo cliente</button>
        <button type="button" class="btn-ghost" (click)="ref.close()">Cancelar</button>
      </div>
    </div>
  `,
})
export class CustomerPicker {
  private readonly dialog = inject(Dialog);
  protected readonly ref = inject<DialogRef<Customer>>(DialogRef);
  protected readonly search = new FormControl('', { nonNullable: true });
  private readonly term = toSignal(this.search.valueChanges.pipe(debounceTime(250), map((v) => v.trim())), { initialValue: '' });
  protected readonly customers = httpResource<Customer[]>(
    () => ({ url: `${API_BASE}/customers`, params: new HttpParams().set('search', this.term()) }),
    { defaultValue: [] },
  );

  protected create(): void {
    this.dialog
      .open<Customer>(CustomerDialog, { data: null, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((c): c is Customer => !!c))
      .subscribe((c) => this.ref.close(c));
  }
}

/** elige el producto que va como adicion de una linea (queso extra) */
@Component({
  selector: 'app-add-on-picker',
  imports: [ReactiveFormsModule, NgIcon],
  providers: [provideIcons({ heroMagnifyingGlass })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card flex max-h-[calc(100dvh-2rem)] w-[min(28rem,calc(100vw-2rem))] flex-col shadow-2xl" role="dialog" aria-labelledby="addon-title">
      <div class="space-y-3 border-b border-line px-6 py-4">
        <h2 id="addon-title" class="text-lg font-bold">Adición para {{ data.parentName }}</h2>
        <div class="relative">
          <ng-icon name="heroMagnifyingGlass" size="18" class="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-faint" />
          <label for="addon-search" class="sr-only">Buscar producto</label>
          <input id="addon-search" type="search" [formControl]="search" placeholder="Buscar producto" class="field-input pl-10" />
        </div>
      </div>
      <ul class="flex-1 divide-y divide-line overflow-y-auto">
        @for (p of visible(); track p.id) {
          <li>
            <button type="button" class="flex w-full items-center justify-between gap-3 px-6 py-3 text-left transition hover:bg-surface-2" (click)="ref.close(p)">
              <span>
                <span class="block font-semibold">{{ p.name }}</span>
                <span class="block text-xs text-muted">{{ p.categoryName }}</span>
              </span>
              <span class="font-semibold tabular-nums">{{ money(p.price) }}</span>
            </button>
          </li>
        } @empty {
          <li class="px-6 py-8 text-center text-sm text-muted">No hay productos con esa búsqueda.</li>
        }
      </ul>
      <div class="flex justify-end border-t border-line px-6 py-4">
        <button type="button" class="btn-ghost" (click)="ref.close()">Cancelar</button>
      </div>
    </div>
  `,
})
export class AddOnPicker {
  protected readonly data = inject<{ parentName: string; products: Product[] }>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Product>>(DialogRef);
  protected readonly money = formatPrice;
  protected readonly search = new FormControl('', { nonNullable: true });
  private readonly term = toSignal(this.search.valueChanges.pipe(map((v) => v.trim().toLowerCase())), { initialValue: '' });

  // primero las categorias que parecen de adiciones
  protected readonly visible = computed(() => {
    const term = this.term();
    const matches = this.data.products.filter((p) => !term || p.name.toLowerCase().includes(term));
    const isAddOn = (p: Product): number => (/adici|extra|topping/i.test(p.categoryName) ? 0 : 1);
    return [...matches].sort((a, b) => isAddOn(a) - isAddOn(b) || a.name.localeCompare(b.name));
  });
}

/** cierre de caja (muestra lo esperado y pide lo contado y devuelve el turno cerrado) */
@Component({
  selector: 'app-close-cash-dialog',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="close()" class="card flex w-[min(30rem,calc(100vw-2rem))] flex-col shadow-2xl" aria-labelledby="close-title">
      <div class="border-b border-line px-6 py-4">
        <h2 id="close-title" class="text-lg font-bold">{{ closed() ? 'Caja cerrada' : 'Cerrar caja' }}</h2>
        <p class="text-sm text-muted">Turno de {{ session().userName }} · abierto {{ time(session().openedAt) }}</p>
      </div>

      <div class="space-y-4 px-6 py-5 text-sm">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-danger" role="alert">{{ error() }}</p>
        }
        <dl class="space-y-1">
          <div class="flex justify-between"><dt class="text-muted">Ventas</dt><dd>{{ session().salesCount }}{{ session().voidedCount ? ' (' + session().voidedCount + ' anuladas aparte)' : '' }}</dd></div>
          <div class="flex justify-between"><dt class="text-muted">Total vendido</dt><dd class="font-semibold tabular-nums">{{ money(session().salesTotal) }}</dd></div>
          @for (m of session().byMethod; track m.method) {
            <div class="flex justify-between"><dt class="text-muted">{{ methodLabel(m.method) }}</dt><dd class="tabular-nums">{{ money(m.amount) }}</dd></div>
          }
          <div class="flex justify-between border-t border-line pt-2"><dt class="text-muted">Base</dt><dd class="tabular-nums">{{ money(session().openingCash) }}</dd></div>
          @if (session().cashOut > 0) {
            <div class="flex justify-between"><dt class="text-muted">Salidas de caja (gastos y pagos)</dt><dd class="tabular-nums">−{{ money(session().cashOut) }}</dd></div>
          }
          <div class="flex justify-between font-semibold"><dt>Efectivo esperado en el cajón</dt><dd class="tabular-nums">{{ money(session().expectedCash) }}</dd></div>
        </dl>

        @if (!closed()) {
          <div>
            <label for="counted" class="field-label">Efectivo contado</label>
            <input id="counted" type="number" inputmode="decimal" min="0" class="field-input tabular-nums" [(ngModel)]="counted" name="counted" required />
          </div>
          <div>
            <label for="close-notes" class="field-label">Notas <span class="font-normal text-faint">(opcional)</span></label>
            <input id="close-notes" class="field-input" [(ngModel)]="notes" name="notes" maxlength="300" />
          </div>
        }
        <p class="text-base" aria-live="polite">
          Diferencia:
          <strong class="tabular-nums" [class]="difference() < 0 ? 'text-danger' : difference() > 0 ? 'text-success' : ''">
            {{ difference() > 0 ? '+' : '' }}{{ money(difference()) }}
          </strong>
          <span class="text-xs text-muted">{{ difference() < 0 ? '(falta)' : difference() > 0 ? '(sobra)' : '(cuadra)' }}</span>
        </p>
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        @if (closed()) {
          <button type="button" class="btn-primary" (click)="ref.close(closed() ?? undefined)">Listo</button>
        } @else {
          <button type="button" class="btn-secondary" (click)="ref.close()" [disabled]="saving()">Cancelar</button>
          <button type="submit" class="btn-primary" [disabled]="saving() || counted === null || counted < 0">{{ saving() ? 'Cerrando…' : 'Cerrar caja' }}</button>
        }
      </div>
    </form>
  `,
})
export class CloseCashDialog {
  private readonly http = inject(HttpClient);
  private readonly initial = inject<CashSession>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<CashSession>>(DialogRef);
  protected readonly money = formatPrice;

  protected readonly closed = signal<CashSession | null>(null);
  protected readonly session = computed(() => this.closed() ?? this.initial);
  protected counted: number | null = null;
  protected notes = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected difference(): number {
    const closed = this.closed();
    if (closed) return closed.difference ?? 0;
    return Math.round(((this.counted ?? 0) - this.initial.expectedCash) * 100) / 100;
  }

  protected methodLabel(method: PaymentMethod): string {
    return PAYMENT_METHODS.find((m) => m.value === method)?.label ?? method;
  }

  protected time(iso: string): string {
    return new Intl.DateTimeFormat('es-CO', { hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
  }

  protected close(): void {
    if (this.counted === null || this.counted < 0) return;
    this.saving.set(true);
    this.error.set(null);
    this.http
      .post<CashSession>(`${API_BASE}/cash-sessions/current/close`, { countedCash: Number(this.counted), notes: this.notes.trim() || null }, { context: silentErrors() })
      .subscribe({
        next: (session) => {
          this.closed.set(session);
          this.saving.set(false);
        },
        error: (e: unknown) => {
          this.error.set(getErrorMessage(e));
          this.saving.set(false);
        },
      });
  }
}
