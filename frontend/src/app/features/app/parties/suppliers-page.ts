import { Dialog } from '@angular/cdk/dialog';
import { HttpClient, HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroEyeSlash, heroMagnifyingGlass, heroPencilSquare, heroPlus, heroTrash, heroTruck } from '@ng-icons/heroicons/outline';
import { debounceTime, filter, map, switchMap } from 'rxjs';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { ConfirmService, DIALOG_DEFAULTS } from '../../../shared/confirm';
import { Supplier } from '../business.models';
import { SupplierDialog } from './supplier-dialog';

@Component({
  selector: 'app-suppliers-page',
  imports: [ReactiveFormsModule, NgIcon],
  providers: [provideIcons({ heroEyeSlash, heroMagnifyingGlass, heroPencilSquare, heroPlus, heroTrash, heroTruck })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Proveedores</h1>
          <p class="mt-1 text-muted">A quién le compras la mercancía.</p>
        </div>
        <button type="button" class="btn-primary" (click)="open()">
          <ng-icon name="heroPlus" size="18" /> Nuevo proveedor
        </button>
      </div>

      <div class="relative mt-8 max-w-md">
        <ng-icon name="heroMagnifyingGlass" size="18" class="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-faint" />
        <label for="supplier-search" class="sr-only">Buscar proveedor por nombre, documento o contacto</label>
        <input id="supplier-search" type="search" [formControl]="search" placeholder="Nombre, documento o contacto" class="field-input pl-10" />
      </div>

      @if (suppliers.isLoading() && suppliers.value().length === 0) {
        <div class="card mt-4 h-48 skeleton !bg-surface-2" aria-label="Cargando proveedores"></div>
      } @else if (suppliers.error()) {
        <div class="card mt-4 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar los proveedores.</p>
          <button type="button" class="btn-secondary mt-4" (click)="suppliers.reload()">Reintentar</button>
        </div>
      } @else if (suppliers.value().length === 0) {
        <div class="card mt-4 border-dashed p-10 text-center">
          <ng-icon name="heroTruck" size="32" class="text-faint" />
          <p class="mt-3 font-semibold">{{ search.value ? 'Ningún proveedor coincide con la búsqueda.' : 'Aún no hay proveedores' }}</p>
        </div>
      } @else {
        <ul class="card mt-4 divide-y divide-line">
          @for (s of suppliers.value(); track s.id) {
            <li class="flex items-center gap-4 px-5 py-4" [class.opacity-60]="!s.isActive">
              <div class="min-w-0 flex-1">
                <p class="flex items-center gap-2 font-semibold">
                  <span class="truncate">{{ s.name }}</span>
                  @if (!s.isActive) {
                    <span class="inline-flex shrink-0 items-center gap-1 text-xs font-normal text-faint"><ng-icon name="heroEyeSlash" size="13" /> inactivo</span>
                  }
                </p>
                <p class="truncate text-sm text-muted">
                  {{ s.documentType }} {{ s.documentNumber }}@if (s.contactName) { · {{ s.contactName }} }@if (s.phone) { · {{ s.phone }} }
                </p>
              </div>
              <div class="flex shrink-0 gap-0.5">
                <button type="button" class="btn-ghost px-2.5" (click)="open(s)" [attr.aria-label]="'Editar ' + s.name">
                  <ng-icon name="heroPencilSquare" size="18" />
                </button>
                <button type="button" class="btn-ghost px-2.5 hover:text-danger" (click)="remove(s)" [attr.aria-label]="'Eliminar ' + s.name">
                  <ng-icon name="heroTrash" size="18" />
                </button>
              </div>
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class SuppliersPage {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly search = new FormControl('', { nonNullable: true });
  private readonly term = toSignal(this.search.valueChanges.pipe(debounceTime(250), map((v) => v.trim())), { initialValue: '' });

  protected readonly suppliers = httpResource<Supplier[]>(
    () => ({ url: `${API_BASE}/suppliers`, params: new HttpParams().set('search', this.term()) }),
    { defaultValue: [] },
  );

  protected open(supplier: Supplier | null = null): void {
    this.dialog
      .open<Supplier>(SupplierDialog, { data: supplier, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((saved): saved is Supplier => !!saved))
      .subscribe((saved) => {
        this.toasts.success(supplier ? `"${saved.name}" actualizado.` : `"${saved.name}" agregado.`);
        this.suppliers.reload();
      });
  }

  protected remove(supplier: Supplier): void {
    this.confirm
      .ask({
        title: `¿Eliminar a ${supplier.name}?`,
        message: 'Si solo dejaste de comprarle, mejor edítalo y desactívalo.',
        confirmText: 'Eliminar',
        danger: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.http.delete<void>(`${API_BASE}/suppliers/${supplier.id}`)),
      )
      .subscribe(() => {
        this.toasts.success(`${supplier.name} eliminado.`);
        this.suppliers.reload();
      });
  }
}
