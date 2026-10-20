import { Dialog } from '@angular/cdk/dialog';
import { HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroBanknotes, heroPlus } from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatIsoDate, localIsoDate } from '../../../shared/dates';
import { formatPrice } from '../../../shared/format';
import { VoidDialog, VoidDialogData } from '../../../shared/void-dialog';
import { EXPENSE_CATEGORIES, Expense, ExpenseCategory, PAYMENT_METHODS, PaymentMethod } from '../business.models';
import { NewExpenseDialog, NewExpenseDialogData } from './expense-dialogs';

@Component({
  selector: 'app-expenses-page',
  imports: [FormsModule, NgIcon],
  providers: [provideIcons({ heroBanknotes, heroPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-5xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Gastos</h1>
          <p class="mt-1 text-muted">{{ auth.isOwner() ? 'Arriendo, servicios, nómina y todo lo que sale fuera de las compras.' : 'El efectivo que sacaste de tu caja.' }}</p>
        </div>
        <button type="button" class="btn-primary" (click)="create()"><ng-icon name="heroPlus" size="18" /> {{ auth.isOwner() ? 'Nuevo gasto' : 'Salida de caja' }}</button>
      </div>

      <div class="mt-8 flex flex-wrap items-end gap-3">
        <div>
          <label for="expenses-from" class="field-label">Desde</label>
          <input id="expenses-from" type="date" class="field-input" [ngModel]="from()" (ngModelChange)="from.set($event)" [max]="to()" />
        </div>
        <div>
          <label for="expenses-to" class="field-label">Hasta</label>
          <input id="expenses-to" type="date" class="field-input" [ngModel]="to()" (ngModelChange)="to.set($event)" [min]="from()" />
        </div>
        <div>
          <label for="expenses-category" class="field-label">Categoría</label>
          <select id="expenses-category" class="field-input" [ngModel]="category()" (ngModelChange)="category.set($event)">
            <option value="">Todas</option>
            @for (c of categories; track c.value) {
              <option [value]="c.value">{{ c.label }}</option>
            }
          </select>
        </div>
      </div>

      @if (expenses.isLoading() && expenses.value().length === 0) {
        <div class="card mt-4 h-48 animate-pulse bg-surface-2" aria-label="Cargando gastos"></div>
      } @else if (expenses.error()) {
        <div class="card mt-4 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar los gastos.</p>
          <button type="button" class="btn-secondary mt-4" (click)="expenses.reload()">Reintentar</button>
        </div>
      } @else if (expenses.value().length === 0) {
        <div class="card mt-4 border-dashed p-10 text-center">
          <ng-icon name="heroBanknotes" size="32" class="text-faint" />
          <p class="mt-3 font-semibold">No hay gastos en esas fechas.</p>
        </div>
      } @else {
        <div class="mt-4 flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted">
          <p>Total: <strong class="text-ink tabular-nums">{{ money(total()) }}</strong> (sin anulados)</p>
          @if (fromRegisterTotal() > 0) {
            <p>Salió de la caja: <strong class="text-ink tabular-nums">{{ money(fromRegisterTotal()) }}</strong></p>
          }
        </div>
        <section class="card mt-2 overflow-hidden" aria-label="Gastos">
          <div class="overflow-x-auto">
            <table class="w-full min-w-[40rem] text-left text-sm">
              <thead class="border-b border-line bg-surface-2 text-xs tracking-wider text-muted uppercase">
                <tr>
                  <th scope="col" class="px-5 py-3 font-semibold">Fecha</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Descripción</th>
                  <th scope="col" class="px-5 py-3 font-semibold">Pago</th>
                  <th scope="col" class="px-5 py-3 text-right font-semibold">Valor</th>
                  @if (auth.isOwner()) { <th scope="col" class="px-5 py-3"><span class="sr-only">Acciones</span></th> }
                </tr>
              </thead>
              <tbody class="divide-y divide-line">
                @for (e of expenses.value(); track e.id) {
                  <tr [class.opacity-60]="e.isVoided">
                    <td class="px-5 py-3 whitespace-nowrap text-muted">{{ date(e.date) }}</td>
                    <td class="px-5 py-3">
                      <p class="font-medium">{{ e.description }}</p>
                      <p class="text-xs text-faint">
                        {{ categoryLabel(e.category) }} · {{ e.createdByName }}
                        @if (e.isVoided) { · <span class="text-danger">anulado por {{ e.voidedBy }}: {{ e.voidReason }}</span> }
                      </p>
                    </td>
                    <td class="px-5 py-3 whitespace-nowrap text-muted">
                      {{ methodLabel(e.method) }}
                      @if (e.fromCashRegister) { <span class="ml-1 rounded-full bg-surface-2 px-2 py-0.5 text-xs">de la caja</span> }
                    </td>
                    <td class="px-5 py-3 text-right whitespace-nowrap">
                      @if (e.isVoided) { <span class="mr-2 rounded-full bg-danger/15 px-2 py-0.5 text-xs font-semibold text-danger">Anulado</span> }
                      <span class="font-semibold tabular-nums" [class.line-through]="e.isVoided">{{ money(e.amount) }}</span>
                    </td>
                    @if (auth.isOwner()) {
                      <td class="px-5 py-3 text-right">
                        @if (!e.isVoided) {
                          <button type="button" class="btn-ghost px-2 py-1 text-xs hover:text-danger" (click)="voidExpense(e)" [attr.aria-label]="'Anular el gasto ' + e.description">Anular</button>
                        }
                      </td>
                    }
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
export class ExpensesPage {
  protected readonly auth = inject(AuthService);
  private readonly dialog = inject(Dialog);
  private readonly toasts = inject(ToastService);
  protected readonly money = formatPrice;
  protected readonly date = formatIsoDate;
  protected readonly categories = EXPENSE_CATEGORIES;

  protected readonly to = signal(localIsoDate());
  protected readonly from = signal(localIsoDate().slice(0, 8) + '01');
  protected readonly category = signal<ExpenseCategory | ''>('');
  protected readonly expenses = httpResource<Expense[]>(
    () => {
      let params = new HttpParams().set('from', this.from()).set('to', this.to());
      if (this.category()) params = params.set('category', this.category());
      return { url: `${API_BASE}/expenses`, params };
    },
    { defaultValue: [] },
  );

  private readonly active = computed(() => this.expenses.value().filter((e) => !e.isVoided));
  protected readonly total = computed(() => this.active().reduce((sum, e) => sum + e.amount, 0));
  protected readonly fromRegisterTotal = computed(() =>
    this.active().filter((e) => e.fromCashRegister).reduce((sum, e) => sum + e.amount, 0),
  );

  protected categoryLabel(category: ExpenseCategory): string {
    return EXPENSE_CATEGORIES.find((c) => c.value === category)?.label ?? category;
  }

  protected methodLabel(method: PaymentMethod): string {
    return PAYMENT_METHODS.find((m) => m.value === method)?.label ?? method;
  }

  protected create(): void {
    const data: NewExpenseDialogData = { cashOnly: !this.auth.isOwner() };
    this.dialog
      .open<Expense>(NewExpenseDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((e): e is Expense => !!e))
      .subscribe((e) => {
        this.toasts.success(`Gasto de ${this.money(e.amount)} registrado.`);
        this.expenses.reload();
      });
  }

  protected voidExpense(expense: Expense): void {
    const data: VoidDialogData = {
      title: `Anular "${expense.description}"`,
      hint: expense.fromCashRegister ? 'El efectivo vuelve a contar en la caja de la que salió.' : 'El gasto queda en la lista marcado como anulado.',
      url: `${API_BASE}/expenses/${expense.id}/void`,
      confirmText: 'Anular gasto',
    };
    this.dialog
      .open<Expense>(VoidDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((e): e is Expense => !!e))
      .subscribe(() => {
        this.toasts.success('Gasto anulado.');
        this.expenses.reload();
      });
  }
}
