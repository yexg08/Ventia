import { Dialog } from '@angular/cdk/dialog';
import { HttpClient, HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  heroAdjustmentsHorizontal,
  heroArchiveBox,
  heroClock,
  heroExclamationTriangle,
  heroEyeSlash,
  heroMagnifyingGlass,
  heroPencilSquare,
  heroPlus,
  heroTrash,
} from '@ng-icons/heroicons/outline';
import { debounceTime, filter, map, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { ConfirmService, DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatPrice } from '../../../shared/format';
import { BusinessSettings, Category, Product, TAX_OPTIONS, TaxType } from '../business.models';
import { ProductDialog, ProductDialogData } from './product-dialog';
import { StockAdjustmentDialog, StockMovementsDialog } from './stock-dialogs';

@Component({
  selector: 'app-products-page',
  imports: [ReactiveFormsModule, RouterLink, NgIcon],
  providers: [
    provideIcons({
      heroAdjustmentsHorizontal,
      heroArchiveBox,
      heroClock,
      heroExclamationTriangle,
      heroEyeSlash,
      heroMagnifyingGlass,
      heroPencilSquare,
      heroPlus,
      heroTrash,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './products-page.html',
})
export class ProductsPage {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly categoryFilter = new FormControl('', { nonNullable: true });
  protected readonly lowStockOnly = signal(false);

  private readonly term = toSignal(this.search.valueChanges.pipe(debounceTime(250), map((v) => v.trim())), { initialValue: '' });
  private readonly categoryId = toSignal(this.categoryFilter.valueChanges, { initialValue: '' });

  protected readonly categories = httpResource<Category[]>(() => `${API_BASE}/categories`, { defaultValue: [] });
  private readonly business = httpResource<BusinessSettings>(() => `${API_BASE}/business`);
  protected readonly products = httpResource<Product[]>(
    () => {
      let params = new HttpParams();
      if (this.term()) params = params.set('search', this.term());
      if (this.categoryId()) params = params.set('categoryId', this.categoryId());
      if (this.lowStockOnly()) params = params.set('lowStock', true);
      return { url: `${API_BASE}/products`, params };
    },
    { defaultValue: [] },
  );

  protected readonly filtering = computed(() => !!this.term() || !!this.categoryId() || this.lowStockOnly());
  protected readonly lowStockCount = computed(() => this.products.value().filter((p) => p.isLowStock).length);
  protected readonly money = formatPrice;

  protected taxLabel(tax: TaxType): string {
    return TAX_OPTIONS.find((t) => t.value === tax)?.label ?? tax;
  }

  protected open(product: Product | null = null): void {
    const data: ProductDialogData = {
      product,
      categories: this.categories.value(),
      defaultTax: this.business.hasValue() ? this.business.value().defaultTax : 'Inc8',
    };
    this.dialog
      .open<Product>(ProductDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((saved): saved is Product => !!saved))
      .subscribe((saved) => {
        this.toasts.success(product ? `"${saved.name}" actualizado.` : `"${saved.name}" creado.`);
        this.products.reload();
      });
  }

  protected adjust(product: Product): void {
    this.dialog
      .open<Product>(StockAdjustmentDialog, { data: product, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((updated): updated is Product => !!updated))
      .subscribe((updated) => {
        this.toasts.success(`Stock de "${updated.name}": ${updated.stock} unidades.`);
        this.products.reload();
      });
  }

  protected history(product: Product): void {
    this.dialog.open(StockMovementsDialog, { data: product, ...DIALOG_DEFAULTS });
  }

  protected remove(product: Product): void {
    this.confirm
      .ask({
        title: '¿Eliminar este producto?',
        message: `"${product.name}" desaparecerá del catálogo. Solo se puede si aún no tiene movimientos de inventario; si no, desactívalo.`,
        confirmText: 'Eliminar',
        danger: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.http.delete<void>(`${API_BASE}/products/${product.id}`)),
      )
      .subscribe(() => {
        this.toasts.success(`"${product.name}" eliminado.`);
        this.products.reload();
      });
  }
}
