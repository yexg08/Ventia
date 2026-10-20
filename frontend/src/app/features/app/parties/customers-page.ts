import { Dialog } from '@angular/cdk/dialog';
import { HttpClient, HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroMagnifyingGlass, heroPencilSquare, heroPlus, heroTrash, heroUserGroup } from '@ng-icons/heroicons/outline';
import { debounceTime, filter, map, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { ConfirmService, DIALOG_DEFAULTS } from '../../../shared/confirm';
import { Customer } from '../business.models';
import { CustomerDialog } from './customer-dialog';

@Component({
  selector: 'app-customers-page',
  imports: [ReactiveFormsModule, NgIcon],
  providers: [provideIcons({ heroMagnifyingGlass, heroPencilSquare, heroPlus, heroTrash, heroUserGroup })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Clientes</h1>
          <p class="mt-1 text-muted">Para vender a nombre de alguien. Sin cliente, la venta va a consumidor final.</p>
        </div>
        <button type="button" class="btn-primary" (click)="open()">
          <ng-icon name="heroPlus" size="18" /> Nuevo cliente
        </button>
      </div>

      <div class="relative mt-8 max-w-md">
        <ng-icon name="heroMagnifyingGlass" size="18" class="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-faint" />
        <label for="customer-search" class="sr-only">Buscar cliente por nombre, documento o celular</label>
        <input id="customer-search" type="search" [formControl]="search" placeholder="Nombre, documento o celular" class="field-input pl-10" />
      </div>

      @if (customers.isLoading() && customers.value().length === 0) {
        <div class="card mt-4 h-48 skeleton !bg-surface-2" aria-label="Cargando clientes"></div>
      } @else if (customers.error()) {
        <div class="card mt-4 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar los clientes.</p>
          <button type="button" class="btn-secondary mt-4" (click)="customers.reload()">Reintentar</button>
        </div>
      } @else if (customers.value().length === 0) {
        <div class="card mt-4 border-dashed p-10 text-center">
          <ng-icon name="heroUserGroup" size="32" class="text-faint" />
          <p class="mt-3 font-semibold">{{ search.value ? 'Ningún cliente coincide con la búsqueda.' : 'Aún no hay clientes' }}</p>
        </div>
      } @else {
        <ul class="card mt-4 divide-y divide-line">
          @for (c of customers.value(); track c.id) {
            <li class="flex items-center gap-4 px-5 py-4">
              <div class="min-w-0 flex-1">
                <p class="truncate font-semibold">{{ c.name }}</p>
                <p class="truncate text-sm text-muted">
                  {{ c.documentType }} {{ c.documentNumber }}@if (c.phone) { · {{ c.phone }} }@if (c.email) { · {{ c.email }} }
                </p>
                @if (c.notes) {
                  <p class="mt-0.5 truncate text-xs text-faint" [title]="c.notes">{{ c.notes }}</p>
                }
              </div>
              <div class="flex shrink-0 gap-0.5">
                <button type="button" class="btn-ghost px-2.5" (click)="open(c)" [attr.aria-label]="'Editar ' + c.name">
                  <ng-icon name="heroPencilSquare" size="18" />
                </button>
                @if (auth.isOwner()) {
                  <button type="button" class="btn-ghost px-2.5 hover:text-danger" (click)="remove(c)" [attr.aria-label]="'Eliminar ' + c.name">
                    <ng-icon name="heroTrash" size="18" />
                  </button>
                }
              </div>
            </li>
          }
        </ul>
        @if (customers.value().length === 200) {
          <p class="mt-3 text-sm text-muted">Se muestran los primeros 200. Usa la búsqueda para encontrar a alguien.</p>
        }
      }
    </div>
  `,
})
export class CustomersPage {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly search = new FormControl('', { nonNullable: true });
  private readonly term = toSignal(this.search.valueChanges.pipe(debounceTime(250), map((v) => v.trim())), { initialValue: '' });

  protected readonly customers = httpResource<Customer[]>(
    () => ({ url: `${API_BASE}/customers`, params: new HttpParams().set('search', this.term()) }),
    { defaultValue: [] },
  );

  protected open(customer: Customer | null = null): void {
    this.dialog
      .open<Customer>(CustomerDialog, { data: customer, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((saved): saved is Customer => !!saved))
      .subscribe((saved) => {
        this.toasts.success(customer ? `"${saved.name}" actualizado.` : `"${saved.name}" agregado.`);
        this.customers.reload();
      });
  }

  // derecho de supresion (Ley 1581) a peticion del cliente
  protected remove(customer: Customer): void {
    this.confirm
      .ask({
        title: `¿Eliminar a ${customer.name}?`,
        message: 'Se borran sus datos personales del negocio. Hazlo cuando el cliente lo pida o si lo registraste por error.',
        confirmText: 'Eliminar',
        danger: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.http.delete<void>(`${API_BASE}/customers/${customer.id}`)),
      )
      .subscribe(() => {
        this.toasts.success(`${customer.name} eliminado.`);
        this.customers.reload();
      });
  }
}
