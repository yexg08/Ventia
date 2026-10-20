using System.Net;
using Ventia.Domain.Entities;
using Ventia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Tests.Integration;

/// <summary>
/// los datos de ejemplo (opcionales y solo para probar en local) cumplen las mismas reglas que lo que registra la
/// aplicacion y se cargan una sola vez
/// </summary>
[Collection(ApiCollection.Name)]
public class DemoSeederTests(ApiFactory factory)
{
    private const string DemoPassword = "Demo2026Ventia";

    private async Task<Guid> SeedAsync()
    {
        await DemoSeeder.SeedAsync(factory.Services, DemoPassword);
        return await factory.WithDbAsync(null, (db, _) =>
            db.Tenants.Where(t => t.Name == DemoSeeder.BusinessName).Select(t => t.Id).SingleAsync());
    }

    [Fact]
    public async Task Demo_data_follows_the_same_rules_as_the_app()
    {
        var tenantId = await SeedAsync();

        await factory.WithDbAsync(tenantId, async (db, _) =>
        {
            // el stock es la suma de sus movimientos y nunca quedo negativo
            foreach (var product in await db.Products.Where(p => p.TracksStock).ToListAsync())
            {
                var movements = await db.StockMovements.Where(m => m.ProductId == product.Id).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync();
                Assert.Equal(product.Stock, movements.Sum(m => m.Quantity));
                Assert.DoesNotContain(movements, m => m.StockAfter < 0);
            }

            // consecutivos sin saltos y el contador del negocio en el ultimo
            var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
            var numbers = await db.Sales.OrderBy(s => s.Number).Select(s => s.Number).ToListAsync();
            Assert.True(numbers.Count > 400, "debe haber un mes de ventas");
            Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
            Assert.Equal(numbers.Count, tenant.LastSaleNumber);
            Assert.Equal(await db.Purchases.CountAsync(), tenant.LastPurchaseNumber);
            Assert.Equal(await db.Quotes.CountAsync(), tenant.LastQuoteNumber);

            // cada venta cuadra (pagos igual al total y base mas impuesto igual al total)
            var sales = await db.Sales.Include(s => s.Lines).Include(s => s.Payments).ToListAsync();
            Assert.All(sales, s =>
            {
                Assert.Equal(s.Total, s.Payments.Sum(p => p.Amount));
                Assert.Equal(s.Total, s.Subtotal + s.TaxTotal);
                Assert.All(s.Lines, l => Assert.NotNull(l.UnitCost));
            });
            Assert.Contains(sales, s => s.Status == SaleStatus.Voided);

            // el efectivo esperado de cada cierre es base mas efectivo de ventas no anuladas menos salidas
            foreach (var session in await db.CashSessions.Where(s => s.ClosedAt != null).ToListAsync())
            {
                var cash = sales.Where(s => s.CashSessionId == session.Id && s.Status == SaleStatus.Completed)
                    .Sum(s => s.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount));
                var cashOut = await db.Expenses.Where(e => e.CashSessionId == session.Id && !e.IsVoided).SumAsync(e => e.Amount);
                Assert.Equal(session.OpeningCash + cash - cashOut, session.ExpectedCash);
            }

            // lo pagado de cada compra es la suma de sus pagos
            foreach (var purchase in await db.Purchases.ToListAsync())
            {
                var paid = await db.SupplierPayments.Where(p => p.PurchaseId == purchase.Id && !p.IsVoided).SumAsync(p => p.Amount);
                Assert.Equal(paid, purchase.PaidAmount);
                Assert.Equal(purchase.Total, await db.PurchaseLines.Where(l => l.PurchaseId == purchase.Id).SumAsync(l => l.LineTotal));
            }

            // las fechas quedaron en el pasado (el historial cubre el mes)
            Assert.True(await db.Sales.MinAsync(s => s.CreatedAt) < DateTime.UtcNow.AddDays(-25));
            return 0;
        });

        // cargarlos otra vez no duplica nada
        var count = await factory.WithDbAsync(tenantId, (db, _) => db.Sales.CountAsync());
        await DemoSeeder.SeedAsync(factory.Services, DemoPassword);
        Assert.Equal(count, await factory.WithDbAsync(tenantId, (db, _) => db.Sales.CountAsync()));
    }

    [Fact]
    public async Task Demo_accounts_work_with_their_roles()
    {
        await SeedAsync();

        var owner = await factory.LoginAsync(DemoSeeder.OwnerEmail, DemoPassword);
        var report = await ApiFactory.Json(await owner.GetAsync("/api/reports/sales"));
        Assert.True(report.GetProperty("salesCount").GetInt32() > 0);
        Assert.True(report.GetProperty("profit").GetProperty("netProfit").GetDecimal() != 0);
        Assert.Equal(0, report.GetProperty("profit").GetProperty("linesWithoutCost").GetInt32());
        Assert.True((await ApiFactory.Json(await owner.GetAsync("/api/payables"))).GetArrayLength() >= 2);
        // el dueno encuentra su caja abierta para vender de una vez
        await ApiFactory.Expect(await owner.GetAsync("/api/cash-sessions/current"), HttpStatusCode.OK);

        var cashier = await factory.LoginAsync(DemoSeeder.CashierEmail, DemoPassword);
        await ApiFactory.Expect(await cashier.GetAsync("/api/reports/sales"), HttpStatusCode.Forbidden);
        var products = await ApiFactory.Json(await cashier.GetAsync("/api/products"));
        Assert.Equal(18, products.GetArrayLength());
        Assert.All(products.EnumerateArray(), p => Assert.Equal(System.Text.Json.JsonValueKind.Null, p.GetProperty("cost").ValueKind));
    }
}
