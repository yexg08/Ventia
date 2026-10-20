using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Reports;

public record ReportQuery(DateOnly? From, DateOnly? To);

public record DaySalesDto(DateOnly Date, int Count, decimal Total);

public record HourSalesDto(int Hour, int Count, decimal Total);

public record OrderTypeSalesDto(OrderType OrderType, int Count, decimal Total);

public record SellerSalesDto(string SellerName, int Count, decimal Total);

public record MethodTotalDto(PaymentMethod Method, decimal Amount);

/// <summary>impuesto cobrado en el periodo (lo que el negocio tendria que declarar)</summary>
public record TaxSummaryDto(TaxType Tax, decimal Base, decimal Amount);

/// <param name="Cost">costo de lo vendido (null si alguna venta del producto no tenia costo)</param>
public record TopProductDto(Guid ProductId, string Name, int Quantity, decimal Total, decimal NetSales, decimal? Cost, decimal? Profit);

public record ExpenseCategoryTotalDto(ExpenseCategory Category, decimal Amount);

/// <param name="Difference">suma de sobrantes (positivo) y faltantes (negativo) de sus cierres</param>
public record CashierDifferenceDto(string UserName, int Sessions, decimal Difference);

/// <param name="NetSales">ventas sin impuestos y con los descuentos aplicados</param>
/// <param name="CostOfSales">costo promedio de lo vendido congelado al momento de cada venta</param>
/// <param name="LinesWithoutCost">lineas vendidas de productos sin costo (la utilidad sale mas alta de lo real)</param>
public record ProfitDto(decimal NetSales, decimal CostOfSales, decimal GrossProfit, decimal Expenses, decimal NetProfit, int LinesWithoutCost);

/// <param name="PreviousTotal">total vendido en el periodo anterior de la misma duracion (para comparar)</param>
/// <param name="Purchases">compras de mercancia (no se restan de la utilidad porque entran al inventario)</param>
public record SalesReportDto(
    DateOnly From, DateOnly To, int SalesCount, int VoidedCount, decimal Total, decimal NetSales, decimal TaxTotal,
    decimal DiscountTotal, decimal AverageTicket, decimal PreviousTotal, int PreviousCount, decimal Purchases,
    IReadOnlyList<DaySalesDto> ByDay, IReadOnlyList<HourSalesDto> ByHour, IReadOnlyList<MethodTotalDto> ByMethod,
    IReadOnlyList<OrderTypeSalesDto> ByOrderType, IReadOnlyList<SellerSalesDto> BySeller, IReadOnlyList<TaxSummaryDto> Taxes,
    IReadOnlyList<TopProductDto> TopProducts, IReadOnlyList<ExpenseCategoryTotalDto> ExpensesByCategory,
    IReadOnlyList<CashierDifferenceDto> CashDifferences, ProfitDto Profit);

public record InventoryItemDto(
    Guid ProductId, string Name, string CategoryName, int Stock, int MinStock, decimal? Cost, decimal Value, bool IsLow);

/// <param name="Value">stock por costo promedio de los productos que llevan inventario</param>
public record InventoryReportDto(int Products, int Units, decimal Value, int WithoutCost, int LowStock, IReadOnlyList<InventoryItemDto> Items);

public enum ExportKind { Sales, Products, Expenses, Purchases, Inventory }

public record CsvFile(string FileName, byte[] Content);

public interface IReportsService
{
    Task<SalesReportDto> GetSalesAsync(ReportQuery query, CancellationToken ct);
    Task<InventoryReportDto> GetInventoryAsync(CancellationToken ct);
    Task<CsvFile> ExportAsync(ExportKind kind, ReportQuery query, CancellationToken ct);
}

/// <summary>reportes y exportaciones del negocio de la sesion (solo el dueno) con los dias en la hora local del negocio</summary>
public class ReportsService(IAppDbContext db, ITenantContext tenant, TimeProvider timeProvider) : IReportsService
{
    private const int MaxDays = 366;
    private const int TopProductsCount = 10;

    private sealed record Range(DateOnly From, DateOnly To, DateTime Start, DateTime End, TimeZoneInfo Zone);

    public async Task<SalesReportDto> GetSalesAsync(ReportQuery query, CancellationToken ct)
    {
        var range = await RangeAsync(query, ct);
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.CreatedAt >= range.Start && s.CreatedAt < range.End)
            .Select(s => new { s.Id, s.CreatedAt, s.SellerName, s.OrderType, s.Status, s.Total, s.Subtotal, s.TaxTotal, s.DiscountTotal })
            .ToListAsync(ct);
        var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();
        var completedSales = CompletedSales(range);

        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => completedSales.Any(s => s.Id == l.SaleId))
            .Select(l => new { l.ProductId, l.ProductName, l.Quantity, l.LineTotal, l.LineBase, l.LineTax, l.Tax, l.UnitCost })
            .ToListAsync(ct);
        var byMethod = await db.SalePayments.AsNoTracking()
            .Where(p => completedSales.Any(s => s.Id == p.SaleId))
            .GroupBy(p => p.Method)
            .Select(g => new MethodTotalDto(g.Key, g.Sum(p => p.Amount)))
            .ToListAsync(ct);

        // periodo anterior de la misma duracion
        var days = range.To.DayNumber - range.From.DayNumber + 1;
        var previousStart = BusinessTime.StartOfDayUtc(range.From.AddDays(-days), range.Zone);
        var previous = db.Sales.Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= previousStart && s.CreatedAt < range.Start);
        var previousTotal = await previous.SumAsync(s => s.Total, ct);
        var previousCount = await previous.CountAsync(ct);

        var purchases = await db.Purchases
            .Where(p => p.Status == PurchaseStatus.Completed && p.Date >= range.From && p.Date <= range.To)
            .SumAsync(p => p.Total, ct);
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => !e.IsVoided && e.Date >= range.From && e.Date <= range.To)
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseCategoryTotalDto(g.Key, g.Sum(e => e.Amount)))
            .ToListAsync(ct);
        var closedSessions = await db.CashSessions.AsNoTracking()
            .Where(s => s.ClosedAt >= range.Start && s.ClosedAt < range.End)
            .Select(s => new { s.UserName, s.CountedCash, s.ExpectedCash })
            .ToListAsync(ct);

        var local = completed.Select(s => new { Sale = s, At = BusinessTime.ToLocal(s.CreatedAt, range.Zone) }).ToList();
        var byDate = local.GroupBy(x => DateOnly.FromDateTime(x.At)).ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(x => x.Sale.Total)));
        var byDay = Enumerable.Range(0, days)
            .Select(i => range.From.AddDays(i))
            .Select(d => byDate.TryGetValue(d, out var v) ? new DaySalesDto(d, v.Count, v.Total) : new DaySalesDto(d, 0, 0))
            .ToList();
        var byHourMap = local.GroupBy(x => x.At.Hour).ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(x => x.Sale.Total)));
        var byHour = Enumerable.Range(0, 24)
            .Select(h => byHourMap.TryGetValue(h, out var v) ? new HourSalesDto(h, v.Count, v.Total) : new HourSalesDto(h, 0, 0))
            .ToList();

        var topProducts = lines
            .GroupBy(l => l.ProductId)
            .Select(g =>
            {
                var netSales = g.Sum(l => l.LineBase);
                decimal? cost = g.All(l => l.UnitCost is not null) ? g.Sum(l => l.UnitCost!.Value * l.Quantity) : null;
                return new TopProductDto(g.Key, g.First().ProductName, g.Sum(l => l.Quantity), g.Sum(l => l.LineTotal), netSales, cost, netSales - cost);
            })
            .OrderByDescending(p => p.Total).ThenByDescending(p => p.Quantity)
            .Take(TopProductsCount)
            .ToList();

        var netSalesTotal = completed.Sum(s => s.Subtotal);
        var costOfSales = lines.Where(l => l.UnitCost is not null).Sum(l => l.UnitCost!.Value * l.Quantity);
        var expensesTotal = expenses.Sum(e => e.Amount);
        var grossProfit = netSalesTotal - costOfSales;
        var profit = new ProfitDto(netSalesTotal, costOfSales, grossProfit, expensesTotal, grossProfit - expensesTotal,
            lines.Count(l => l.UnitCost is null));
        var total = completed.Sum(s => s.Total);

        return new SalesReportDto(
            range.From, range.To, completed.Count, sales.Count - completed.Count, total, netSalesTotal,
            completed.Sum(s => s.TaxTotal), completed.Sum(s => s.DiscountTotal),
            completed.Count == 0 ? 0 : Math.Round(total / completed.Count, 0, MidpointRounding.AwayFromZero),
            previousTotal, previousCount, purchases, byDay, byHour,
            byMethod.OrderByDescending(m => m.Amount).ToList(),
            completed.GroupBy(s => s.OrderType).Select(g => new OrderTypeSalesDto(g.Key, g.Count(), g.Sum(s => s.Total))).OrderByDescending(o => o.Total).ToList(),
            completed.GroupBy(s => s.SellerName).Select(g => new SellerSalesDto(g.Key, g.Count(), g.Sum(s => s.Total))).OrderByDescending(s => s.Total).ToList(),
            lines.Where(l => l.Tax != TaxType.None).GroupBy(l => l.Tax).Select(g => new TaxSummaryDto(g.Key, g.Sum(l => l.LineBase), g.Sum(l => l.LineTax))).OrderBy(t => t.Tax).ToList(),
            topProducts,
            expenses.OrderByDescending(e => e.Amount).ToList(),
            closedSessions.GroupBy(s => s.UserName)
                .Select(g => new CashierDifferenceDto(g.Key, g.Count(), g.Sum(s => (s.CountedCash ?? 0) - (s.ExpectedCash ?? 0))))
                .OrderBy(c => c.Difference).ToList(),
            profit);
    }

    public async Task<InventoryReportDto> GetInventoryAsync(CancellationToken ct)
    {
        var items = await InventoryItemsAsync(ct);
        return new InventoryReportDto(
            items.Count, items.Sum(i => i.Stock), items.Sum(i => i.Value), items.Count(i => i.Cost is null), items.Count(i => i.IsLow),
            items);
    }

    public async Task<CsvFile> ExportAsync(ExportKind kind, ReportQuery query, CancellationToken ct)
    {
        if (kind == ExportKind.Inventory)
        {
            var today = BusinessTime.Today(await ZoneAsync(ct), timeProvider);
            return new CsvFile($"inventario_{Csv.Date(today)}.csv", (await InventoryCsvAsync(ct)).ToBytes());
        }

        var range = await RangeAsync(query, ct);
        var csv = kind switch
        {
            ExportKind.Sales => await SalesCsvAsync(range, ct),
            ExportKind.Products => await ProductsCsvAsync(range, ct),
            ExportKind.Expenses => await ExpensesCsvAsync(range, ct),
            ExportKind.Purchases => await PurchasesCsvAsync(range, ct),
            _ => throw new NotFoundException("El reporte")
        };
        var name = kind switch
        {
            ExportKind.Sales => "ventas",
            ExportKind.Products => "productos-vendidos",
            ExportKind.Expenses => "gastos",
            _ => "compras"
        };
        return new CsvFile($"{name}_{Csv.Date(range.From)}_{Csv.Date(range.To)}.csv", csv.ToBytes());
    }

    private async Task<Csv> SalesCsvAsync(Range range, CancellationToken ct)
    {
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.CreatedAt >= range.Start && s.CreatedAt < range.End)
            .OrderBy(s => s.Number)
            .Select(s => new
            {
                s.Number, s.CreatedAt, s.SellerName, s.OrderType, s.CustomerName, s.CustomerDocument, s.Subtotal, s.TaxTotal,
                s.DiscountTotal, s.Total, s.Status, s.VoidReason,
                Cash = s.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
                Card = s.Payments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount),
                Transfer = s.Payments.Where(p => p.Method == PaymentMethod.Transfer).Sum(p => p.Amount)
            })
            .ToListAsync(ct);

        var csv = new Csv("Número", "Fecha", "Vendedor", "Tipo de pedido", "Cliente", "Documento", "Subtotal", "Impuestos",
            "Descuentos", "Total", "Efectivo", "Tarjeta", "Transferencia", "Estado", "Motivo de anulación");
        foreach (var s in sales)
            csv.Row(s.Number.ToString(), Csv.Date(BusinessTime.ToLocal(s.CreatedAt, range.Zone)), Csv.Text(s.SellerName),
                Labels.Of(s.OrderType), Csv.Text(s.CustomerName), Csv.Text(s.CustomerDocument), Csv.Number(s.Subtotal),
                Csv.Number(s.TaxTotal), Csv.Number(s.DiscountTotal), Csv.Number(s.Total), Csv.Number(s.Cash), Csv.Number(s.Card),
                Csv.Number(s.Transfer), s.Status == SaleStatus.Voided ? "Anulada" : "Completada", Csv.Text(s.VoidReason));
        return csv;
    }

    private async Task<Csv> ProductsCsvAsync(Range range, CancellationToken ct)
    {
        var completedSales = CompletedSales(range);
        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => completedSales.Any(s => s.Id == l.SaleId))
            .Join(db.Sales, l => l.SaleId, s => s.Id, (l, s) => new { Line = l, s.Number, s.CreatedAt })
            .OrderBy(x => x.Number).ThenBy(x => x.Line.Position)
            .ToListAsync(ct);

        var csv = new Csv("Venta", "Fecha", "Producto", "Adición", "Cantidad", "Precio unitario", "Descuento", "Impuesto",
            "Base", "Valor del impuesto", "Total", "Costo unitario", "Utilidad");
        foreach (var x in lines)
        {
            var l = x.Line;
            csv.Row(x.Number.ToString(), Csv.Date(BusinessTime.ToLocal(x.CreatedAt, range.Zone)), Csv.Text(l.ProductName),
                l.ParentLineId is null ? "No" : "Sí", l.Quantity.ToString(), Csv.Number(l.UnitPrice), Csv.Number(l.DiscountAmount),
                Labels.Of(l.Tax), Csv.Number(l.LineBase), Csv.Number(l.LineTax), Csv.Number(l.LineTotal), Csv.Number(l.UnitCost),
                Csv.Number(l.UnitCost is { } cost ? l.LineBase - cost * l.Quantity : null));
        }
        return csv;
    }

    private async Task<Csv> ExpensesCsvAsync(Range range, CancellationToken ct)
    {
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.Date >= range.From && e.Date <= range.To)
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAt)
            .ToListAsync(ct);

        var csv = new Csv("Fecha", "Categoría", "Descripción", "Medio de pago", "Salió de la caja", "Registrado por", "Valor", "Estado", "Motivo de anulación");
        foreach (var e in expenses)
            csv.Row(Csv.Date(e.Date), Labels.Of(e.Category), Csv.Text(e.Description), Labels.Of(e.Method),
                e.CashSessionId is null ? "No" : "Sí", Csv.Text(e.CreatedByName), Csv.Number(e.Amount),
                e.IsVoided ? "Anulado" : "Vigente", Csv.Text(e.VoidReason));
        return csv;
    }

    private async Task<Csv> PurchasesCsvAsync(Range range, CancellationToken ct)
    {
        var purchases = await db.Purchases.AsNoTracking()
            .Where(p => p.Date >= range.From && p.Date <= range.To)
            .OrderBy(p => p.Number)
            .ToListAsync(ct);

        var csv = new Csv("Número", "Fecha", "Proveedor", "Factura del proveedor", "Total", "Pagado", "Saldo", "Estado");
        foreach (var p in purchases)
            csv.Row(p.Number.ToString(), Csv.Date(p.Date), Csv.Text(p.SupplierName), Csv.Text(p.SupplierInvoice), Csv.Number(p.Total),
                Csv.Number(p.PaidAmount), Csv.Number(p.Status == PurchaseStatus.Completed ? p.Total - p.PaidAmount : 0),
                p.Status == PurchaseStatus.Voided ? "Anulada" : "Vigente");
        return csv;
    }

    private async Task<Csv> InventoryCsvAsync(CancellationToken ct)
    {
        var csv = new Csv("Producto", "Categoría", "Stock", "Stock mínimo", "Costo promedio", "Valor", "Stock bajo");
        foreach (var i in await InventoryItemsAsync(ct))
            csv.Row(Csv.Text(i.Name), Csv.Text(i.CategoryName), i.Stock.ToString(), i.MinStock.ToString(), Csv.Number(i.Cost),
                Csv.Number(i.Value), i.IsLow ? "Sí" : "No");
        return csv;
    }

    private async Task<List<InventoryItemDto>> InventoryItemsAsync(CancellationToken ct)
    {
        var rows = await db.Products.AsNoTracking()
            .Where(p => p.TracksStock && p.IsActive)
            .Join(db.Categories, p => p.CategoryId, c => c.Id, (p, c) => new { p.Id, p.Name, Category = c.Name, p.Stock, p.MinStock, p.Cost })
            .ToListAsync(ct);
        return rows
            .Select(p => new InventoryItemDto(p.Id, p.Name, p.Category, p.Stock, p.MinStock, p.Cost, p.Stock * (p.Cost ?? 0), p.Stock <= p.MinStock))
            .OrderByDescending(i => i.Value).ThenBy(i => i.Name)
            .ToList();
    }

    private IQueryable<Sale> CompletedSales(Range range) =>
        db.Sales.Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= range.Start && s.CreatedAt < range.End);

    /// <summary>por defecto el mes en curso hasta hoy</summary>
    private async Task<Range> RangeAsync(ReportQuery query, CancellationToken ct)
    {
        var zone = await ZoneAsync(ct);
        var to = query.To ?? BusinessTime.Today(zone, timeProvider);
        var from = query.From ?? new DateOnly(to.Year, to.Month, 1);
        if (from > to) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) throw new BusinessRuleException("Consulta máximo un año a la vez.");
        return new Range(from, to, BusinessTime.StartOfDayUtc(from, zone), BusinessTime.StartOfDayUtc(to.AddDays(1), zone), zone);
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        return BusinessTime.Zone(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(ct));
    }

    /// <summary>nombres en espanol para las columnas del CSV</summary>
    private static class Labels
    {
        public static string Of(OrderType type) => type switch
        {
            OrderType.DineIn => "Mesa",
            OrderType.Delivery => "Domicilio",
            _ => "Para llevar"
        };

        public static string Of(PaymentMethod method) => method switch
        {
            PaymentMethod.Cash => "Efectivo",
            PaymentMethod.Card => "Tarjeta",
            _ => "Transferencia"
        };

        public static string Of(TaxType tax) => tax switch
        {
            TaxType.Inc8 => "INC 8 %",
            TaxType.Iva19 => "IVA 19 %",
            TaxType.Iva5 => "IVA 5 %",
            _ => "Sin impuesto"
        };

        public static string Of(ExpenseCategory category) => category switch
        {
            ExpenseCategory.Rent => "Arriendo",
            ExpenseCategory.Utilities => "Servicios públicos",
            ExpenseCategory.Payroll => "Nómina",
            ExpenseCategory.Supplies => "Insumos y desechables",
            ExpenseCategory.Maintenance => "Mantenimiento",
            ExpenseCategory.Marketing => "Publicidad",
            ExpenseCategory.Taxes => "Impuestos",
            _ => "Otros"
        };
    }
}
