import { HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  heroArrowRight,
  heroBanknotes,
  heroChartBar,
  heroCheck,
  heroDocumentText,
  heroExclamationTriangle,
  heroInboxArrowDown,
  heroShoppingCart,
  heroUserGroup,
} from '@ng-icons/heroicons/outline';
import { AuthService } from '../../core/auth/auth.service';
import { API_BASE } from '../../core/http/api';
import { localIsoDate } from '../../shared/dates';
import { formatPrice } from '../../shared/format';
import { BusinessSettings, CashSession, Category, InventoryReport, Payable, Product, SalesReport, UserAccount } from './business.models';

interface Step {
  title: string;
  text: string;
  done: boolean;
  link: string | null;
  action: string;
}

interface QuickAction {
  link: string;
  label: string;
  hint: string;
  icon: string;
}

@Component({
  selector: 'app-home',
  imports: [RouterLink, NgIcon],
  providers: [
    provideIcons({
      heroArrowRight,
      heroBanknotes,
      heroChartBar,
      heroCheck,
          heroDocumentText,
      heroExclamationTriangle,
      heroInboxArrowDown,
      heroShoppingCart,
      heroUserGroup,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.html',
})
export class Home {
  protected readonly auth = inject(AuthService);
  protected readonly money = formatPrice;

  private readonly today = localIsoDate();
  private readonly owner = computed(() => this.auth.isOwner());

  // resumen del dia (solo el dueno puede leer reportes y cuentas por pagar)
  protected readonly report = httpResource<SalesReport>(() =>
    this.owner() ? { url: `${API_BASE}/reports/sales`, params: new HttpParams().set('from', this.today).set('to', this.today) } : undefined,
  );
  protected readonly payables = httpResource<Payable[]>(() => (this.owner() ? `${API_BASE}/payables` : undefined), { defaultValue: [] });
  protected readonly inventory = httpResource<InventoryReport>(() => (this.owner() ? `${API_BASE}/reports/inventory` : undefined));
  // turno de caja de quien entra (204 sin cuerpo = no hay caja abierta)
  protected readonly session = httpResource<CashSession | null>(() => `${API_BASE}/cash-sessions/current`);

  // primeros pasos (solo el dueno puede leer cuentas y ajustes)
  private readonly business = httpResource<BusinessSettings>(() => (this.owner() ? `${API_BASE}/business` : undefined));
  private readonly categories = httpResource<Category[]>(() => (this.owner() ? `${API_BASE}/categories` : undefined));
  private readonly products = httpResource<Product[]>(() => (this.owner() ? `${API_BASE}/products` : undefined));
  private readonly users = httpResource<UserAccount[]>(() => (this.owner() ? `${API_BASE}/users` : undefined));

  protected readonly firstName = computed(() => this.auth.user()?.fullName.split(' ')[0] ?? '');
  protected readonly greeting = computed(() => {
    const hour = new Date().getHours();
    return hour < 12 ? 'Buenos días' : hour < 19 ? 'Buenas tardes' : 'Buenas noches';
  });
  protected readonly todayLabel = new Intl.DateTimeFormat('es-CO', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date());

  protected readonly owed = computed(() => this.payables.value().reduce((sum, p) => sum + p.balance, 0));

  protected readonly actions = computed<QuickAction[]>(() =>
    this.owner()
      ? [
          { link: '/app/vender', label: 'Vender', hint: 'Abre la caja y cobra', icon: 'heroShoppingCart' },
          { link: '/app/compras', label: 'Registrar compra', hint: 'Sube el stock', icon: 'heroInboxArrowDown' },
          { link: '/app/gastos', label: 'Registrar gasto', hint: 'Arriendo, servicios…', icon: 'heroBanknotes' },
          { link: '/app/reportes', label: 'Ver reportes', hint: 'Cuánto ganaste', icon: 'heroChartBar' },
        ]
      : [
          { link: '/app/vender', label: 'Vender', hint: 'Abre la caja y cobra', icon: 'heroShoppingCart' },
          { link: '/app/ventas', label: 'Mis ventas', hint: 'Comprobantes de hoy', icon: 'heroDocumentText' },
          { link: '/app/clientes', label: 'Clientes', hint: 'Registra a quien pide factura', icon: 'heroUserGroup' },
          { link: '/app/gastos', label: 'Salidas de caja', hint: 'Efectivo que sacaste', icon: 'heroBanknotes' },
        ],
  );

  protected readonly steps = computed<Step[]>(() => {
    const business = this.business.hasValue() ? this.business.value() : null;
    const categoryCount = this.categories.hasValue() ? this.categories.value().length : 0;
    const productCount = this.products.hasValue() ? this.products.value().length : 0;
    const cashierCount = this.users.hasValue() ? this.users.value().filter((u) => u.role === 'Cashier').length : 0;
    return [
      { title: 'Crea tu negocio', text: 'Ya tienes tu cuenta de dueño.', done: true, link: null, action: '' },
      {
        title: 'Completa los datos del negocio',
        text: 'NIT, dirección y teléfono: salen en los comprobantes.',
        done: !!business?.taxId && !!business?.address,
        link: '/app/configuracion',
        action: 'Completar',
      },
      {
        title: 'Organiza tu menú en categorías',
        text: categoryCount > 0 ? `Tienes ${categoryCount} categoría(s).` : 'Por ejemplo: hamburguesas, perros, bebidas y adiciones.',
        done: categoryCount > 0,
        link: '/app/categorias',
        action: 'Crear',
      },
      {
        title: 'Agrega tus productos',
        text: productCount > 0 ? `Tienes ${productCount} producto(s).` : 'Precio con impuesto incluido y, si quieres, el inventario.',
        done: productCount > 0,
        link: '/app/productos',
        action: 'Agregar',
      },
      {
        title: 'Crea las cuentas de tus cajeros',
        text: cashierCount > 0 ? `Tienes ${cashierCount} cajero(s).` : 'Cada cajero entra con su propia cuenta.',
        done: cashierCount > 0,
        link: '/app/usuarios',
        action: 'Agregar',
      },
    ];
  });
  /** los primeros pasos se muestran hasta que esten todos hechos (y solo cuando ya cargaron) */
  protected readonly pendingSteps = computed(() => {
    const loaded = this.business.hasValue() && this.categories.hasValue() && this.products.hasValue() && this.users.hasValue();
    return loaded && this.steps().some((s) => !s.done);
  });
  protected readonly doneCount = computed(() => this.steps().filter((s) => s.done).length);
}
