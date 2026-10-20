using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Ventia.Tests.Integration;

/// <summary>reportes (las cifras cuadran con las ventas y el costo de lo vendido no cambia con compras posteriores) y CSV</summary>
[Collection(ApiCollection.Name)]
public class ReportsTests(ApiFactory factory)
{
    private sealed record Shop(ApiFactory.Business Business, Guid Burger, Guid Soda, Guid Fries);

    /// <summary>hamburguesa 18.000 (costo 7.000) gaseosa 4.000 (costo 2.200) y papas 6.000 sin costo y con la caja abierta</summary>
    private async Task<Shop> ShopAsync()
    {
        var business = await factory.RegisterBusinessAsync();
        var food = await ApiFactory.CreateCategoryAsync(business.Client, "Comida");
        var burger = await ProductAsync(business.Client, ApiFactory.NewProduct(food, "Hamburguesa", price: 18000, cost: 7000, tracksStock: false, initialStock: null));
        var soda = await ProductAsync(business.Client, ApiFactory.NewProduct(food, "Gaseosa", price: 4000, cost: 2200, initialStock: 20));
        var fries = await ProductAsync(business.Client, ApiFactory.NewProduct(food, "Papas", price: 6000, cost: null, tracksStock: false, initialStock: null));
        await ApiFactory.Expect(await business.Client.PostAsJsonAsync("/api/cash-sessions", new { openingCash = 50000 }), HttpStatusCode.Created);
        return new Shop(business, burger, soda, fries);
    }

    private static async Task<Guid> ProductAsync(HttpClient client, object product)
    {
        var response = await client.PostAsJsonAsync("/api/products", product);
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        return (await ApiFactory.Json(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SellAsync(HttpClient client, (Guid Product, int Quantity)[] lines, string method, decimal total, string orderType = "TakeAway")
    {
        var response = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = Guid.NewGuid(), orderType, tableLabel = (string?)null,
            deliveryAddress = orderType == "Delivery" ? "Calle 1" : null, customerId = (Guid?)null,
            lines = lines.Select(l => new { productId = l.Product, quantity = l.Quantity, discountPercent = 0, note = (string?)null, parentIndex = (int?)null }),
            payments = new[] { new { method, amount = total } }, cashTendered = 0, notes = (string?)null
        });
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        return await ApiFactory.Json(response);
    }

    private static async Task<JsonElement> ReportAsync(HttpClient client) =>
        await ApiFactory.Json(await client.GetAsync("/api/reports/sales"));

    [Fact]
    public async Task The_sales_report_adds_up_and_ignores_voided_sales()
    {
        var shop = await ShopAsync();
        var client = shop.Business.Client;
        await SellAsync(client, [(shop.Burger, 2), (shop.Soda, 1)], "Cash", 40000);        // 40.000
        await SellAsync(client, [(shop.Fries, 1)], "Card", 6000, "Delivery");               // 6.000
        var voided = await SellAsync(client, [(shop.Burger, 5)], "Transfer", 90000);        // anulada
        await client.PostAsJsonAsync($"/api/sales/{voided.GetProperty("id").GetGuid()}/void", new { reason = "Error" });
        await client.PostAsJsonAsync("/api/expenses", new { date = (string?)null, category = "Rent", description = "Arriendo", amount = 10000, method = "Transfer", fromCashRegister = false });

        var report = await ReportAsync(client);
        Assert.Equal(2, report.GetProperty("salesCount").GetInt32());
        Assert.Equal(1, report.GetProperty("voidedCount").GetInt32());
        Assert.Equal(46000m, report.GetProperty("total").GetDecimal());
        Assert.Equal(23000m, report.GetProperty("averageTicket").GetDecimal());
        Assert.Equal(report.GetProperty("total").GetDecimal(),
            report.GetProperty("netSales").GetDecimal() + report.GetProperty("taxTotal").GetDecimal());

        // por medio de pago y por dia (hoy esta en la serie)
        var methods = report.GetProperty("byMethod").EnumerateArray().ToDictionary(m => m.GetProperty("method").GetString()!, m => m.GetProperty("amount").GetDecimal());
        Assert.Equal(40000m, methods["Cash"]);
        Assert.Equal(6000m, methods["Card"]);
        Assert.False(methods.ContainsKey("Transfer"));
        Assert.Equal(46000m, report.GetProperty("byDay").EnumerateArray().Sum(d => d.GetProperty("total").GetDecimal()));
        Assert.Equal(24, report.GetProperty("byHour").GetArrayLength());

        // productos mas vendidos (la hamburguesa anulada no cuenta)
        var top = report.GetProperty("topProducts").EnumerateArray().ToList();
        Assert.Equal("Hamburguesa", top[0].GetProperty("name").GetString());
        Assert.Equal(2, top[0].GetProperty("quantity").GetInt32());
        Assert.Equal(14000m, top[0].GetProperty("cost").GetDecimal());
        var fries = top.Single(p => p.GetProperty("name").GetString() == "Papas");
        Assert.Equal(JsonValueKind.Null, fries.GetProperty("cost").ValueKind);

        // utilidad = ventas sin impuesto menos costo (7.000 x 2 + 2.200) menos gastos
        var profit = report.GetProperty("profit");
        var netSales = profit.GetProperty("netSales").GetDecimal();
        Assert.Equal(16200m, profit.GetProperty("costOfSales").GetDecimal());
        Assert.Equal(netSales - 16200m, profit.GetProperty("grossProfit").GetDecimal());
        Assert.Equal(10000m, profit.GetProperty("expenses").GetDecimal());
        Assert.Equal(netSales - 16200m - 10000m, profit.GetProperty("netProfit").GetDecimal());
        Assert.Equal(1, profit.GetProperty("linesWithoutCost").GetInt32());

        // el INC 8 % cobrado
        var tax = Assert.Single(report.GetProperty("taxes").EnumerateArray());
        Assert.Equal("Inc8", tax.GetProperty("tax").GetString());
        Assert.Equal(report.GetProperty("taxTotal").GetDecimal(), tax.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task The_cost_of_a_sale_is_frozen_when_the_average_cost_changes_later()
    {
        var shop = await ShopAsync();
        var client = shop.Business.Client;
        await SellAsync(client, [(shop.Soda, 2)], "Cash", 8000);

        // una compra cara sube el costo promedio de la gaseosa
        var supplier = await ApiFactory.Json(await client.PostAsJsonAsync("/api/suppliers", new
        {
            documentType = "NIT", documentNumber = "800197268-4", name = "Distribuidora", contactName = (string?)null,
            phone = (string?)null, email = (string?)null, address = (string?)null, notes = (string?)null, isActive = true
        }));
        await ApiFactory.Expect(await client.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = supplier.GetProperty("id").GetGuid(), supplierInvoice = (string?)null, date = (string?)null, notes = (string?)null,
            lines = new[] { new { productId = shop.Soda, quantity = 18, unitCost = 3800 } }, paidWith = (string?)null, paidFromCashRegister = false
        }), HttpStatusCode.Created);
        Assert.NotEqual(2200m, (await ApiFactory.Json(await client.GetAsync($"/api/products/{shop.Soda}"))).GetProperty("cost").GetDecimal());

        var report = await ReportAsync(client);
        Assert.Equal(4400m, report.GetProperty("profit").GetProperty("costOfSales").GetDecimal());
        Assert.Equal(68400m, report.GetProperty("purchases").GetDecimal());
    }

    [Fact]
    public async Task The_inventory_report_values_stock_at_average_cost()
    {
        var shop = await ShopAsync();
        await SellAsync(shop.Business.Client, [(shop.Soda, 16)], "Cash", 64000);

        var inventory = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/reports/inventory"));
        var soda = Assert.Single(inventory.GetProperty("items").EnumerateArray());
        Assert.Equal(4, soda.GetProperty("stock").GetInt32());
        Assert.Equal(8800m, soda.GetProperty("value").GetDecimal());
        Assert.True(soda.GetProperty("isLow").GetBoolean());
        Assert.Equal(8800m, inventory.GetProperty("value").GetDecimal());
        Assert.Equal(1, inventory.GetProperty("lowStock").GetInt32());
    }

    [Fact]
    public async Task Csv_exports_open_in_excel_and_neutralize_formulas()
    {
        var shop = await ShopAsync();
        var client = shop.Business.Client;
        await SellAsync(client, [(shop.Burger, 1)], "Cash", 18000);
        await client.PostAsJsonAsync("/api/expenses", new
        {
            date = (string?)null, category = "Supplies", description = "=HYPERLINK(\"http://x\";\"clic\")", amount = 1500.5, method = "Card", fromCashRegister = false
        });

        var response = await client.GetAsync("/api/reports/export/expenses");
        await ApiFactory.Expect(response, HttpStatusCode.OK);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("gastos_", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Fecha;Categoría;Descripción;Medio de pago;Salió de la caja;Registrado por;Valor;Estado;Motivo de anulación", lines[0]);
        // la formula queda como texto (apostrofo y comillas dobladas) y el valor con coma decimal
        Assert.Contains(";\"'=HYPERLINK(\"\"http://x\"\";\"\"clic\"\")\";", lines[1]);
        Assert.Contains(";1500,5;", lines[1]);

        foreach (var kind in new[] { "sales", "products", "purchases", "inventory" })
            await ApiFactory.Expect(await client.GetAsync($"/api/reports/export/{kind}"), HttpStatusCode.OK);
        var products = await (await client.GetAsync("/api/reports/export/products")).Content.ReadAsStringAsync();
        Assert.Contains("Hamburguesa;No;1;18000;0;INC 8 %", products);
        await ApiFactory.Expect(await client.GetAsync("/api/reports/export/clientes"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await client.GetAsync("/api/reports/export/1"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reports_are_for_the_owner_and_only_see_their_business()
    {
        var shop = await ShopAsync();
        await SellAsync(shop.Business.Client, [(shop.Burger, 1)], "Cash", 18000);
        var other = await factory.RegisterBusinessAsync();
        var cashier = (await factory.CreateCashierAsync(shop.Business.Client)).Client;

        Assert.Equal(0m, (await ReportAsync(other.Client)).GetProperty("total").GetDecimal());
        Assert.Equal(0, (await ApiFactory.Json(await other.Client.GetAsync("/api/reports/inventory"))).GetProperty("products").GetInt32());
        var otherCsv = await (await other.Client.GetAsync("/api/reports/export/sales")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Hamburguesa", otherCsv);
        Assert.Single(otherCsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));

        await ApiFactory.Expect(await cashier.GetAsync("/api/reports/sales"), HttpStatusCode.Forbidden);
        await ApiFactory.Expect(await cashier.GetAsync("/api/reports/export/sales"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_report_covers_at_most_one_year_and_fills_empty_days()
    {
        var shop = await ShopAsync();
        var tooLong = await shop.Business.Client.GetAsync("/api/reports/sales?from=2025-01-01&to=2026-06-30");
        await ApiFactory.Expect(tooLong, HttpStatusCode.Conflict);

        var week = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/reports/sales?from=2026-03-01&to=2026-03-07"));
        Assert.Equal(7, week.GetProperty("byDay").GetArrayLength());
        Assert.Equal("2026-03-01", week.GetProperty("byDay")[0].GetProperty("date").GetString());
    }
}
