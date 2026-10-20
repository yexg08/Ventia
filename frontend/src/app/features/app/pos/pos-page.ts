import { Dialog } from '@angular/cdk/dialog';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  heroArrowUpTray,
  heroBanknotes,
  heroChatBubbleBottomCenterText,
  heroLockClosed,
  heroMagnifyingGlass,
  heroMinus,
  heroPlus,
  heroPlusCircle,
  heroReceiptPercent,
  heroTrash,
  heroUser,
  heroXMark,
} from '@ng-icons/heroicons/outline';
import { filter } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE, getErrorMessage, silentErrors } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { CartLineMath, cartTotals, lineAmounts } from '../../../shared/cart';
import { DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatPrice } from '../../../shared/format';
import { CashSession, Category, Customer, Expense, ORDER_TYPES, OrderType, Product, Sale } from '../business.models';
import { NewExpenseDialog, NewExpenseDialogData } from '../expenses/expense-dialogs';
import { PaymentDialog, PaymentDialogData } from './payment-dialog';
import { AddOnPicker, CloseCashDialog, CustomerPicker } from './pos-dialogs';
import { ReceiptDialog, ReceiptDialogData } from './receipt-dialog';

interface CartLine extends CartLineMath {
  key: string;
  product: Product;
  note: string;
  /** clave de la linea principal si es una adicion */
  parentKey: string | null;
}

@Component({
  selector: 'app-pos-page',
  imports: [FormsModule, RouterLink, NgIcon],
  providers: [
    provideIcons({
      heroArrowUpTray,
      heroBanknotes,
      heroChatBubbleBottomCenterText,
      heroLockClosed,
      heroMagnifyingGlass,
      heroMinus,
      heroPlus,
      heroPlusCircle,
      heroReceiptPercent,
      heroTrash,
      heroUser,
      heroXMark,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pos-page.html',
})
export class PosPage {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly toasts = inject(ToastService);

  protected readonly money = formatPrice;
  protected readonly orderTypes = ORDER_TYPES;

  // turno de caja (204 sin cuerpo = no hay caja abierta)
  protected readonly session = httpResource<CashSession | null>(() => `${API_BASE}/cash-sessions/current`);
  protected readonly categories = httpResource<Category[]>(() => `${API_BASE}/categories`, { defaultValue: [] });
  protected readonly products = httpResource<Product[]>(() => `${API_BASE}/products`, { defaultValue: [] });

  protected openingCash = 0;
  protected readonly opening = signal(false);

  // catalogo
  protected readonly categoryId = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly activeCategories = computed(() => {
    const withProducts = new Set(this.sellable().map((p) => p.categoryId));
    return this.categories.value().filter((c) => c.isActive && withProducts.has(c.id));
  });
  private readonly sellable = computed(() => {
    const activeCategories = new Set(this.categories.value().filter((c) => c.isActive).map((c) => c.id));
    return this.products.value().filter((p) => p.isActive && activeCategories.has(p.categoryId));
  });
  protected readonly visibleProducts = computed(() => {
    const term = this.search().trim().toLowerCase();
    const category = this.categoryId();
    return this.sellable().filter((p) =>
      term ? p.name.toLowerCase().includes(term) || p.code?.toLowerCase() === term || p.barcode === term : !category || p.categoryId === category,
    );
  });

  // pedido
  protected readonly cart = signal<CartLine[]>([]);
  protected readonly orderType = signal<OrderType>('TakeAway');
  protected tableLabel = '';
  protected deliveryAddress = '';
  protected readonly customer = signal<Customer | null>(null);
  protected readonly editingKey = signal<string | null>(null);
  protected readonly cartError = signal<string | null>(null);

  /** lineas en orden de pantalla (cada producto seguido de sus adiciones) */
  protected readonly orderedLines = computed(() => {
    const lines = this.cart();
    return lines
      .filter((l) => !l.parentKey)
      .flatMap((parent) => [parent, ...lines.filter((l) => l.parentKey === parent.key)]);
  });
  protected readonly totals = computed(() => cartTotals(this.cart()));
  protected readonly itemCount = computed(() => this.cart().filter((l) => !l.parentKey).reduce((n, l) => n + l.quantity, 0));

  protected amounts(line: CartLine): ReturnType<typeof lineAmounts> {
    return lineAmounts(line);
  }

  /** unidades de un producto ya puestas en el pedido (para no ofrecer mas de las que hay) */
  protected inCart(productId: string): number {
    return this.cart().filter((l) => l.product.id === productId).reduce((n, l) => n + l.quantity, 0);
  }

  protected available(product: Product): number | null {
    return product.tracksStock ? product.stock - this.inCart(product.id) : null;
  }

  protected openCash(): void {
    this.opening.set(true);
    this.http.post<CashSession>(`${API_BASE}/cash-sessions`, { openingCash: Number(this.openingCash) || 0 }, { context: silentErrors() }).subscribe({
      next: (session) => {
        this.session.set(session);
        this.opening.set(false);
        this.toasts.success('Caja abierta. ¡Buenas ventas!');
      },
      error: (e: unknown) => {
        this.toasts.error(getErrorMessage(e));
        this.opening.set(false);
      },
    });
  }

  protected add(product: Product, parentKey: string | null = null): void {
    const available = this.available(product);
    if (available !== null && available <= 0) {
      this.toasts.error(`No quedan unidades de "${product.name}".`);
      return;
    }
    // el mismo producto sin nota ni descuento suma cantidad en vez de crear otra linea
    const same = this.cart().find((l) => l.product.id === product.id && l.parentKey === parentKey && !l.note && l.discountPercent === 0);
    if (same) {
      this.changeQuantity(same, 1);
      return;
    }
    const line: CartLine = {
      key: crypto.randomUUID(),
      product,
      unitPrice: product.price,
      quantity: 1,
      discountPercent: 0,
      tax: product.tax,
      note: '',
      parentKey,
    };
    this.cart.update((lines) => [...lines, line]);
    this.cartError.set(null);
  }

  /** enter en el buscador agrega el producto si el codigo o el codigo de barras coincide exacto */
  protected addByCode(): void {
    const term = this.search().trim().toLowerCase();
    const match = this.sellable().find((p) => p.barcode === term || p.code?.toLowerCase() === term);
    const single = this.visibleProducts().length === 1 ? this.visibleProducts()[0] : null;
    const product = match ?? single;
    if (product) {
      this.add(product);
      this.search.set('');
    }
  }

  protected changeQuantity(line: CartLine, delta: number): void {
    const next = line.quantity + delta;
    if (next <= 0) {
      this.remove(line);
      return;
    }
    const available = this.available(line.product);
    if (delta > 0 && available !== null && available <= 0) {
      this.toasts.error(`No quedan más unidades de "${line.product.name}".`);
      return;
    }
    this.cart.update((lines) => lines.map((l) => (l.key === line.key ? { ...l, quantity: Math.min(next, 999) } : l)));
  }

  protected update(line: CartLine, changes: Partial<Pick<CartLine, 'note' | 'discountPercent'>>): void {
    const discount = changes.discountPercent;
    const safe = discount === undefined ? changes : { ...changes, discountPercent: Math.min(100, Math.max(0, Number(discount) || 0)) };
    this.cart.update((lines) => lines.map((l) => (l.key === line.key ? { ...l, ...safe } : l)));
  }

  /** quita la linea y sus adiciones */
  protected remove(line: CartLine): void {
    this.cart.update((lines) => lines.filter((l) => l.key !== line.key && l.parentKey !== line.key));
  }

  protected clear(): void {
    this.cart.set([]);
    this.customer.set(null);
    this.tableLabel = '';
    this.deliveryAddress = '';
    this.orderType.set('TakeAway');
    this.editingKey.set(null);
    this.cartError.set(null);
  }

  protected pickAddOn(parent: CartLine): void {
    this.dialog
      .open<Product>(AddOnPicker, { data: { parentName: parent.product.name, products: this.sellable() }, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((p): p is Product => !!p))
      .subscribe((product) => this.add(product, parent.key));
  }

  protected pickCustomer(): void {
    this.dialog
      .open<Customer>(CustomerPicker, DIALOG_DEFAULTS)
      .closed.pipe(filter((c): c is Customer => !!c))
      .subscribe((c) => {
        this.customer.set(c);
        if (!this.deliveryAddress && c.address) this.deliveryAddress = c.address;
      });
  }

  protected charge(): void {
    if (this.cart().length === 0) return;
    if (this.orderType() === 'Delivery' && !this.deliveryAddress.trim()) {
      this.cartError.set('Escribe la dirección del domicilio.');
      return;
    }
    const ordered = this.orderedLines();
    const index = new Map(ordered.map((l, i) => [l.key, i]));
    const data: PaymentDialogData = {
      total: this.totals().total,
      request: {
        orderType: this.orderType(),
        tableLabel: this.orderType() === 'DineIn' ? this.tableLabel.trim() || null : null,
        deliveryAddress: this.orderType() === 'Delivery' ? this.deliveryAddress.trim() : null,
        customerId: this.customer()?.id ?? null,
        notes: null,
        lines: ordered.map((l) => ({
          productId: l.product.id,
          quantity: l.quantity,
          discountPercent: l.discountPercent,
          note: l.note.trim() || null,
          parentIndex: l.parentKey ? (index.get(l.parentKey) ?? null) : null,
        })),
      },
    };
    this.dialog
      .open<Sale>(PaymentDialog, { data, ...DIALOG_DEFAULTS, disableClose: true })
      .closed.pipe(filter((sale): sale is Sale => !!sale))
      .subscribe((sale) => {
        this.clear();
        this.products.reload();
        this.session.reload();
        const receipt: ReceiptDialogData = { sale, saleId: sale.id };
        this.dialog.open(ReceiptDialog, { data: receipt, ...DIALOG_DEFAULTS });
      });
  }

  /** efectivo que sale del cajon (un domicilio o una compra menor) */
  protected cashOut(): void {
    const data: NewExpenseDialogData = { cashOnly: true };
    this.dialog
      .open<Expense>(NewExpenseDialog, { data, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((e): e is Expense => !!e))
      .subscribe((e) => {
        this.toasts.success(`Salida de ${this.money(e.amount)} registrada.`);
        this.session.reload();
      });
  }

  protected closeCash(session: CashSession): void {
    this.session.reload();
    this.dialog
      .open<CashSession>(CloseCashDialog, { data: session, ...DIALOG_DEFAULTS })
      .closed.pipe(filter((closed): closed is CashSession => !!closed))
      .subscribe(() => {
        this.clear();
        this.session.set(null);
      });
  }
}
