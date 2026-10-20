import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  heroArchiveBox,
  heroArrowRight,
  heroBanknotes,
  heroChartBar,
  heroCheck,
  heroCheckCircle,
  heroDocumentArrowDown,
  heroLockClosed,
  heroShoppingCart,
  heroSparkles,
  heroUsers,
} from '@ng-icons/heroicons/outline';
import { AuthService } from '../../core/auth/auth.service';
import { formatPrice } from '../../shared/format';
import { Logo } from '../../shared/logo';
import { ThemeToggle } from '../../shared/theme-toggle';

@Component({
  selector: 'app-landing',
  imports: [RouterLink, NgIcon, Logo, ThemeToggle],
  providers: [
    provideIcons({
      heroArchiveBox,
      heroArrowRight,
      heroBanknotes,
      heroChartBar,
      heroCheck,
      heroCheckCircle,
      heroDocumentArrowDown,
      heroLockClosed,
      heroShoppingCart,
      heroSparkles,
      heroUsers,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './landing.html',
})
export class Landing {
  protected readonly auth = inject(AuthService);
  protected readonly money = formatPrice;
  protected readonly year = new Date().getFullYear();

  // productos de la vista previa de la caja (solo decorativos)
  protected readonly previewProducts = [
    { name: 'Hamburguesa clásica', price: 16000 },
    { name: 'Perro americano', price: 14000 },
    { name: 'Salchipapa', price: 13000 },
    { name: 'Gaseosa 400 ml', price: 4000 },
    { name: 'Papas a la francesa', price: 6000 },
    { name: 'Limonada de coco', price: 9000 },
  ];
  protected readonly previewOrder = [
    { name: 'Hamburguesa clásica', detail: 'Sin cebolla', quantity: 2, total: 32000 },
    { name: '+ Queso extra', detail: '', quantity: 1, total: 3000 },
    { name: 'Gaseosa 400 ml', detail: '', quantity: 2, total: 8000 },
  ];
  protected readonly previewBars = [38, 52, 44, 70, 58, 86, 74];

  protected readonly features = [
    {
      icon: 'heroShoppingCart',
      title: 'Una caja que no se queda atrás',
      text: 'Menú por categorías, lector de código de barras, notas para la cocina, adiciones y pago mixto con el cambio calculado.',
      wide: true,
    },
    { icon: 'heroArchiveBox', title: 'Inventario sin sorpresas', text: 'Cada venta descuenta el stock y nunca deja vender lo que no hay.', wide: false },
    { icon: 'heroChartBar', title: 'Sabes cuánto ganas', text: 'Utilidad real con el costo de cada venta, horas pico y lo más vendido.', wide: false },
    { icon: 'heroBanknotes', title: 'Compras, gastos y caja', text: 'Cuentas por pagar con abonos, salidas de caja y cierres que cuadran solos.', wide: false },
    { icon: 'heroUsers', title: 'Cuentas para tu equipo', text: 'Cada cajero entra con su cuenta y no ve costos ni márgenes.', wide: false },
    {
      icon: 'heroLockClosed',
      title: 'Tus datos son solo tuyos',
      text: 'Cada negocio ve únicamente su información. El aislamiento se aplica en tres capas y está cubierto por pruebas.',
      wide: true,
    },
  ];

  protected readonly guarantees = [
    'El stock nunca queda negativo, ni con dos cajas vendiendo a la vez',
    'Un doble clic al cobrar crea una sola venta',
    'Los consecutivos no tienen saltos ni repetidos',
    'Los abonos nunca superan lo que le debes al proveedor',
  ];

  protected readonly steps = [
    { title: 'Crea tu negocio', text: 'Con tu NIT y el impuesto que cobras: INC u IVA.' },
    { title: 'Arma tu menú', text: 'Categorías, precios y qué productos llevan inventario.' },
    { title: 'Abre la caja y vende', text: 'Tus cajeros entran con su cuenta. Tú ves los reportes.' },
  ];
}
