import { Dialog } from '@angular/cdk/dialog';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroEyeSlash, heroPencilSquare, heroPlus, heroTag, heroTrash } from '@ng-icons/heroicons/outline';
import { filter, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { ConfirmService, DIALOG_DEFAULTS } from '../../../shared/confirm';
import { Category } from '../business.models';
import { CategoryDialog, CategoryDialogData } from './category-dialog';

@Component({
  selector: 'app-categories-page',
  imports: [NgIcon],
  providers: [provideIcons({ heroEyeSlash, heroPencilSquare, heroPlus, heroTag, heroTrash })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Categorías</h1>
          <p class="mt-1 text-muted">Agrupan tu menú en el punto de venta.</p>
        </div>
        @if (auth.isOwner()) {
          <button type="button" class="btn-primary" (click)="open()">
            <ng-icon name="heroPlus" size="18" /> Nueva categoría
          </button>
        }
      </div>

      @if (categories.isLoading() && categories.value().length === 0) {
        <div class="card mt-8 h-48 skeleton !bg-surface-2" aria-label="Cargando categorías"></div>
      } @else if (categories.error()) {
        <div class="card mt-8 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar las categorías.</p>
          <button type="button" class="btn-secondary mt-4" (click)="categories.reload()">Reintentar</button>
        </div>
      } @else if (categories.value().length === 0) {
        <div class="card mt-8 border-dashed p-10 text-center">
          <ng-icon name="heroTag" size="32" class="text-faint" />
          <p class="mt-3 font-semibold">Aún no hay categorías</p>
          <p class="mt-1 text-sm text-muted">
            {{ auth.isOwner() ? 'Crea las de tu menú: hamburguesas, perros, bebidas, adiciones…' : 'El dueño del negocio aún no las ha creado.' }}
          </p>
        </div>
      } @else {
        <ul class="card mt-8 divide-y divide-line">
          @for (category of categories.value(); track category.id) {
            <li class="flex items-center gap-4 px-5 py-4">
              <span class="w-10 shrink-0 text-center text-sm font-semibold text-faint tabular-nums" title="Orden">{{ category.sortOrder }}</span>
              <div class="min-w-0 flex-1">
                <p class="truncate font-semibold" [class.text-muted]="!category.isActive">{{ category.name }}</p>
                @if (!category.isActive) {
                  <p class="mt-0.5 inline-flex items-center gap-1 text-xs text-faint">
                    <ng-icon name="heroEyeSlash" size="13" /> Inactiva: no sale en el punto de venta
                  </p>
                }
              </div>
              @if (auth.isOwner()) {
                <div class="flex shrink-0 gap-1">
                  <button type="button" class="btn-ghost px-2.5" (click)="open(category)" [attr.aria-label]="'Editar ' + category.name">
                    <ng-icon name="heroPencilSquare" size="18" />
                  </button>
                  <button type="button" class="btn-ghost px-2.5 hover:text-danger" (click)="remove(category)" [attr.aria-label]="'Eliminar ' + category.name">
                    <ng-icon name="heroTrash" size="18" />
                  </button>
                </div>
              }
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class CategoriesPage {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly categories = httpResource<Category[]>(() => `${API_BASE}/categories`, { defaultValue: [] });

  // una categoria nueva queda al final (de 10 en 10 para poder meter otras en medio)
  private readonly nextOrder = computed(() => {
    const orders = this.categories.value().map((c) => c.sortOrder);
    return orders.length === 0 ? 0 : Math.min(999, Math.max(...orders) + 10);
  });

  protected open(category: Category | null = null): void {
    const data: CategoryDialogData = { category, nextOrder: this.nextOrder() };
    this.dialog
      .open<Category>(CategoryDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((saved): saved is Category => !!saved))
      .subscribe((saved) => {
        this.toasts.success(category ? `"${saved.name}" actualizada.` : `"${saved.name}" creada.`);
        this.categories.reload();
      });
  }

  protected remove(category: Category): void {
    this.confirm
      .ask({
        title: '¿Eliminar esta categoría?',
        message: `"${category.name}" desaparecerá del punto de venta. Si solo quieres ocultarla un tiempo, edítala y desactívala.`,
        confirmText: 'Eliminar',
        danger: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.http.delete<void>(`${API_BASE}/categories/${category.id}`)),
      )
      .subscribe(() => {
        this.toasts.success(`"${category.name}" eliminada.`);
        this.categories.reload();
      });
  }
}
