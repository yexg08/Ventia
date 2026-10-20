import { HttpClient, HttpParams, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroArrowDownTray, heroArrowTrendingDown, heroArrowTrendingUp, heroChartBar, heroExclamationTriangle } from '@ng-icons/heroicons/outline';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { formatIsoDate, localIsoDate } from '../../../shared/dates';
import { downloadFile } from '../../../shared/download';
import { formatPrice } from '../../../shared/format';
import {
  EXPENSE_CATEGORIES,
  ExpenseCategory,
  ExportKind,
  InventoryReport,
  ORDER_TYPES,
  OrderType,
  PAYMENT_METHODS,
  PaymentMethod,
  SalesReport,
  TAX_OPTIONS,
  TaxType,
} from '../business.models';
import { RANGE_PRESETS, RangePreset, percentChange, presetRange, share } from './report-math';

const MAX_DAYS = 366;

@Component({
  selector: 'app-reports-page',
  imports: [FormsModule, NgIcon, RouterLink],
  providers: [provideIcons({ heroArrowDownTray, heroArrowTrendingDown, heroArrowTrendingUp, heroChartBar, heroExclamationTriangle })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reports-page.html',
})
export class ReportsPage {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(ToastService);
  protected readonly money = formatPrice;
  protected readonly date = formatIsoDate;
  protected readonly share = share;
  protected readonly presets = RANGE_PRESETS;

  private readonly today = localIsoDate();
  protected readonly from = signal(presetRange('month', this.today).from);
  protected readonly to = signal(this.today);
  protected readonly preset = computed<RangePreset | null>(() => {
    const match = RANGE_PRESETS.find((p) => {
      const range = presetRange(p.value, this.today);
      return range.from === this.from() && range.to === this.to();
    });
    return match?.value ?? null;
  });

  protected readonly tooLong = computed(() => {
    const days = (Date.parse(this.to()) - Date.parse(this.from())) / 86_400_000 + 1;
    return days > MAX_DAYS;
  });
  private readonly params = computed(() => new HttpParams().set('from', this.from()).set('to', this.to()));

  protected readonly report = httpResource<SalesReport>(() =>
    this.from() && this.to() && !this.tooLong() ? { url: `${API_BASE}/reports/sales`, params: this.params() } : undefined,
  );
  protected readonly inventory = httpResource<InventoryReport>(() => `${API_BASE}/reports/inventory`);

  protected readonly change = computed(() => {
    const r = this.report.value();
    return r ? percentChange(r.total, r.previousTotal) : null;
  });
  protected readonly maxDay = computed(() => Math.max(1, ...(this.report.value()?.byDay.map((d) => d.total) ?? [])));
  protected readonly maxHour = computed(() => Math.max(1, ...(this.report.value()?.byHour.map((h) => h.total) ?? [])));
  protected readonly bestDay = computed(() => {
    const days = this.report.value()?.byDay ?? [];
    return days.reduce<(typeof days)[number] | null>((best, d) => (d.total > 0 && (!best || d.total > best.total) ? d : best), null);
  });
  protected readonly peakHour = computed(() => {
    const hours = this.report.value()?.byHour ?? [];
    return hours.reduce<(typeof hours)[number] | null>((best, h) => (h.total > 0 && (!best || h.total > best.total) ? h : best), null);
  });
  /** etiquetas del eje de dias (primero ultimo y algunos intermedios) */
  protected readonly dayTicks = computed(() => {
    const days = this.report.value()?.byDay ?? [];
    if (days.length <= 1) return days.map((d) => d.date);
    const step = Math.max(1, Math.ceil(days.length / 6));
    const ticks = days.filter((_, i) => i % step === 0).map((d) => d.date);
    const last = days[days.length - 1].date;
    if (ticks[ticks.length - 1] !== last) ticks.push(last);
    return ticks;
  });
  protected readonly grossMargin = computed(() => {
    const p = this.report.value()?.profit;
    return p ? share(p.grossProfit, p.netSales) : 0;
  });
  protected readonly expensesTotal = computed(() => this.report.value()?.profit.expenses ?? 0);

  protected readonly downloading = signal<ExportKind | null>(null);
  protected readonly exports: { kind: ExportKind; label: string; hint: string }[] = [
    { kind: 'sales', label: 'Ventas', hint: 'Una fila por venta, con pagos y anuladas' },
    { kind: 'products', label: 'Productos vendidos', hint: 'Cada línea con impuesto, costo y utilidad' },
    { kind: 'expenses', label: 'Gastos', hint: 'Con categoría y medio de pago' },
    { kind: 'purchases', label: 'Compras', hint: 'Con lo pagado y el saldo' },
    { kind: 'inventory', label: 'Inventario', hint: 'Stock y valor de hoy (sin fechas)' },
  ];

  protected setPreset(preset: RangePreset): void {
    const range = presetRange(preset, this.today);
    this.from.set(range.from);
    this.to.set(range.to);
  }

  protected barHeight(value: number, max: number): string {
    return value <= 0 ? '0' : `max(${(value / max) * 100}%, 3px)`;
  }

  protected dayLabel(iso: string): string {
    const [y, m, d] = iso.split('-').map(Number);
    return new Intl.DateTimeFormat('es-CO', { day: 'numeric', month: 'short' }).format(new Date(y, m - 1, d));
  }

  protected hourLabel(hour: number): string {
    return `${hour % 12 === 0 ? 12 : hour % 12} ${hour < 12 ? 'a. m.' : 'p. m.'}`;
  }

  protected methodLabel(method: PaymentMethod): string {
    return PAYMENT_METHODS.find((m) => m.value === method)?.label ?? method;
  }

  protected orderTypeLabel(type: OrderType): string {
    return ORDER_TYPES.find((o) => o.value === type)?.label ?? type;
  }

  protected taxLabel(tax: TaxType): string {
    return TAX_OPTIONS.find((t) => t.value === tax)?.label ?? tax;
  }

  protected categoryLabel(category: ExpenseCategory): string {
    return EXPENSE_CATEGORIES.find((c) => c.value === category)?.label ?? category;
  }

  protected download(kind: ExportKind): void {
    if (kind !== 'inventory' && this.tooLong()) return;
    this.downloading.set(kind);
    const params = kind === 'inventory' ? new HttpParams() : this.params();
    // si falla el interceptor de errores ya muestra el aviso
    downloadFile(this.http, `${API_BASE}/reports/export/${kind}`, params, `${kind}.csv`).subscribe({
      next: () => {
        this.downloading.set(null);
        this.toasts.success('Archivo descargado. Ábrelo con Excel o Google Sheets.');
      },
      error: () => this.downloading.set(null),
    });
  }
}
