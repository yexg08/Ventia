using System.Net;
using System.Net.Http.Json;

namespace Ventia.Tests.Integration;

/// <summary>compras (stock y costo promedio) y cotizaciones (precios congelados y conversion en venta una sola vez)</summary>
[Collection(ApiCollection.Name)]
public class DocumentsTests(ApiFactory factory)
{
    private sealed record Shop(ApiFactory.Business Business, Guid Soda, Guid Burger, Guid Supplier);

    /// <summary>gaseosa con 10 unidades a costo 2.200 y hamburguesa sin inventario y un proveedor activo</summary>
    private async Task<Shop> ShopAsync()
    {
        var business = await factory.RegisterBusinessAsync();
        var drinks = await ApiFactory.CreateCategoryAsync(business.Client, "Bebidas");
        var food = await ApiFactory.CreateCategoryAsync(business.Client, "Comida");
        var soda = await IdAsync(await business.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(drinks, "Gaseosa", price: 4000, cost: 2200, initialStock: 10)));
        var burger = await IdAsync(await business.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(food, "Hamburguesa", price: 18000, cost: null, tracksStock: false, initialStock: null)));
        var supplier = await IdAsync(await business.Client.PostAsJsonAsync("/api/suppliers", new
        {
            documentType = "NIT", documentNumber = "800197268-4", name = "Distribuidora Andina", contactName = (string?)null,
            phone = (string?)null, email = (string?)null, address = (string?)null, notes = (string?)null, isActive = true
        }));
        return new Shop(business, soda, burger, supplier);
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response)
    {
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        return (await ApiFactory.Json(response)).GetProperty("id").GetGuid();
    }

    private static object Purchase(Guid supplierId, params object[] lines) =>
        new { supplierId, supplierInvoice = "FV-123", date = (string?)null, lines, notes = (string?)null };

    private static object PurchaseLine(Guid productId, int quantity, decimal unitCost) => new { productId, quantity, unitCost };

    private static async Task<(int Stock, decimal? Cost)> ProductAsync(HttpClient client, Guid id)
    {
        var p = await ApiFactory.Json(await client.GetAsync($"/api/products/{id}"));
        var cost = p.GetProperty("cost");
        return (p.GetProperty("stock").GetInt32(), cost.ValueKind == System.Text.Json.JsonValueKind.Null ? null : cost.GetDecimal());
    }

    private static object Quote(object[] lines, Guid? customerId = null, string? customerName = "Evento Juan", int validDays = 10) =>
        new
        {
            customerId, customerName, validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5)).AddDays(validDays).ToString("yyyy-MM-dd"),
            lines, notes = (string?)null
        };

    private static object QuoteLine(Guid productId, int quantity, decimal discountPercent = 0) => new { productId, quantity, discountPercent };

    private static object Convert(decimal amount, Guid? key = null) => new
    {
        idempotencyKey = key ?? Guid.NewGuid(), orderType = "TakeAway", tableLabel = (string?)null, deliveryAddress = (string?)null,
        payments = new[] { new { method = "Card", amount } }, cashTendered = 0m
    };

    [Fact]
    public async Task A_purchase_raises_the_stock_and_recalculates_the_average_cost()
    {
        var shop = await ShopAsync();

        // 10 unidades a 2.200 mas 20 a 2.500 dan 30 unidades a 2.400
        var response = await shop.Business.Client.PostAsJsonAsync("/api/purchases", Purchase(shop.Supplier, PurchaseLine(shop.Soda, 20, 2500)));
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        var purchase = await ApiFactory.Json(response);
        Assert.Equal(1, purchase.GetProperty("number").GetInt32());
        Assert.Equal(50000m, purchase.GetProperty("total").GetDecimal());
        Assert.Equal("Distribuidora Andina", purchase.GetProperty("supplierName").GetString());

        var (stock, cost) = await ProductAsync(shop.Business.Client, shop.Soda);
        Assert.Equal(30, stock);
        Assert.Equal(2400m, cost);

        var movements = await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/products/{shop.Soda}/movements"));
        Assert.Equal("Purchase", movements[0].GetProperty("type").GetString());
        Assert.Equal("Compra 1", movements[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Without_stock_or_cost_the_purchase_cost_becomes_the_product_cost()
    {
        var shop = await ShopAsync();
        var drinks = await ApiFactory.CreateCategoryAsync(shop.Business.Client, "Jugos");
        var juice = await IdAsync(await shop.Business.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(drinks, "Jugo", price: 6000, cost: null, initialStock: 0)));

        await shop.Business.Client.PostAsJsonAsync("/api/purchases",
            Purchase(shop.Supplier, PurchaseLine(juice, 5, 2000), PurchaseLine(juice, 5, 3000)));
        Assert.Equal((10, 2500m), await ProductAsync(shop.Business.Client, juice));
    }

    [Fact]
    public async Task Purchases_are_only_for_active_suppliers_and_products_with_inventory()
    {
        var shop = await ShopAsync();

        var untracked = await shop.Business.Client.PostAsJsonAsync("/api/purchases", Purchase(shop.Supplier, PurchaseLine(shop.Burger, 5, 1000)));
        await ApiFactory.Expect(untracked, HttpStatusCode.Conflict);
        Assert.Contains("no lleva inventario", (await ApiFactory.Json(untracked)).GetProperty("detail").GetString());

        await shop.Business.Client.PutAsJsonAsync($"/api/suppliers/{shop.Supplier}", new
        {
            documentType = "NIT", documentNumber = "800197268-4", name = "Distribuidora Andina", contactName = (string?)null,
            phone = (string?)null, email = (string?)null, address = (string?)null, notes = (string?)null, isActive = false
        });
        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync("/api/purchases",
            Purchase(shop.Supplier, PurchaseLine(shop.Soda, 5, 1000))), HttpStatusCode.Conflict);

        var cashier = (await factory.CreateCashierAsync(shop.Business.Client)).Client;
        await ApiFactory.Expect(await cashier.GetAsync("/api/purchases"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Voiding_a_purchase_takes_the_units_out_unless_they_were_already_sold()
    {
        var shop = await ShopAsync();
        var first = (await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/purchases",
            Purchase(shop.Supplier, PurchaseLine(shop.Soda, 5, 2000))))).GetProperty("id").GetGuid();
        Assert.Equal(15, (await ProductAsync(shop.Business.Client, shop.Soda)).Stock);

        var voided = await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{first}/void", new { reason = "Llegó equivocada" });
        await ApiFactory.Expect(voided, HttpStatusCode.OK);
        Assert.Equal("Voided", (await ApiFactory.Json(voided)).GetProperty("status").GetString());
        Assert.Equal(10, (await ProductAsync(shop.Business.Client, shop.Soda)).Stock);

        // una compra de 20 de la que ya se vendio casi todo no se puede anular
        var second = (await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/purchases",
            Purchase(shop.Supplier, PurchaseLine(shop.Soda, 20, 2000))))).GetProperty("id").GetGuid();
        await shop.Business.Client.PostAsJsonAsync($"/api/products/{shop.Soda}/stock-adjustments", new { kind = "Out", quantity = 25, reason = "Vendidas" });
        var blocked = await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{second}/void", new { reason = "Error" });
        await ApiFactory.Expect(blocked, HttpStatusCode.Conflict);
        Assert.Contains("No se puede anular la compra 2", (await ApiFactory.Json(blocked)).GetProperty("detail").GetString());
        Assert.Equal(5, (await ProductAsync(shop.Business.Client, shop.Soda)).Stock);
    }

    [Fact]
    public async Task A_supplier_with_purchases_cannot_be_deleted()
    {
        var shop = await ShopAsync();
        await shop.Business.Client.PostAsJsonAsync("/api/purchases", Purchase(shop.Supplier, PurchaseLine(shop.Soda, 1, 2000)));
        await ApiFactory.Expect(await shop.Business.Client.DeleteAsync($"/api/suppliers/{shop.Supplier}"), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_quote_keeps_its_prices_and_converts_into_a_sale_with_them()
    {
        var shop = await ShopAsync();
        var cashier = (await factory.CreateCashierAsync(shop.Business.Client)).Client;

        // el cajero cotiza 10 hamburguesas con 10 por ciento de descuento y 5 gaseosas
        var created = await cashier.PostAsJsonAsync("/api/quotes", Quote([QuoteLine(shop.Burger, 10, 10), QuoteLine(shop.Soda, 5)]));
        await ApiFactory.Expect(created, HttpStatusCode.Created);
        var quote = await ApiFactory.Json(created);
        var id = quote.GetProperty("id").GetGuid();
        Assert.Equal(182000m, quote.GetProperty("total").GetDecimal());

        // despues sube el precio de la hamburguesa pero la cotizacion vigente respeta el precio cotizado
        var food = await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/products/{shop.Burger}"));
        await shop.Business.Client.PutAsJsonAsync($"/api/products/{shop.Burger}",
            ApiFactory.NewProduct(food.GetProperty("categoryId").GetGuid(), "Hamburguesa", price: 20000, cost: null, tracksStock: false, initialStock: null));

        await ApiFactory.Expect(await cashier.PostAsJsonAsync($"/api/quotes/{id}/convert", Convert(182000)), HttpStatusCode.Conflict); // sin caja
        await cashier.PostAsJsonAsync("/api/cash-sessions", new { openingCash = 0 });
        var key = Guid.NewGuid();
        var converted = await cashier.PostAsJsonAsync($"/api/quotes/{id}/convert", Convert(182000, key));
        await ApiFactory.Expect(converted, HttpStatusCode.Created);
        var sale = await ApiFactory.Json(converted);
        Assert.Equal(182000m, sale.GetProperty("total").GetDecimal());
        Assert.Equal("Evento Juan", (await ApiFactory.Json(await cashier.GetAsync($"/api/quotes/{id}"))).GetProperty("customerName").GetString());
        Assert.Equal(5, (await ProductAsync(shop.Business.Client, shop.Soda)).Stock);

        var after = await ApiFactory.Json(await cashier.GetAsync($"/api/quotes/{id}"));
        Assert.Equal("Converted", after.GetProperty("status").GetString());
        Assert.Equal(sale.GetProperty("number").GetInt32(), after.GetProperty("saleNumber").GetInt32());

        // reintentar el mismo cobro devuelve la misma venta y un cobro nuevo no la convierte otra vez
        var retry = await ApiFactory.Json(await cashier.PostAsJsonAsync($"/api/quotes/{id}/convert", Convert(182000, key)));
        Assert.Equal(sale.GetProperty("id").GetGuid(), retry.GetProperty("id").GetGuid());
        await ApiFactory.Expect(await cashier.PostAsJsonAsync($"/api/quotes/{id}/convert", Convert(182000)), HttpStatusCode.Conflict);
        Assert.Equal(5, (await ProductAsync(shop.Business.Client, shop.Soda)).Stock);
    }

    [Fact]
    public async Task Simultaneous_conversions_create_a_single_sale()
    {
        var shop = await ShopAsync();
        await shop.Business.Client.PostAsJsonAsync("/api/cash-sessions", new { openingCash = 0 });
        var id = (await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/quotes", Quote([QuoteLine(shop.Soda, 2)]))))
            .GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            shop.Business.Client.PostAsJsonAsync($"/api/quotes/{id}/convert", Convert(8000))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/sales"))).GetArrayLength());
        Assert.Equal(8, (await ProductAsync(shop.Business.Client, shop.Soda)).Stock);
    }

    [Fact]
    public async Task Expired_or_cancelled_quotes_cannot_be_converted_and_editing_takes_todays_prices()
    {
        var shop = await ShopAsync();
        await shop.Business.Client.PostAsJsonAsync("/api/cash-sessions", new { openingCash = 0 });
        var id = (await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/quotes", Quote([QuoteLine(shop.Soda, 1)]))))
            .GetProperty("id").GetGuid();

        // se vence a mano en la base
        await factory.WithDbAsync(shop.Business.TenantId, (db, _) => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteUpdateAsync(db.Quotes.Where(q => q.Id == id), s => s.SetProperty(q => q.ValidUntil, new DateOnly(2020, 1, 1))));
        var listed = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/quotes"));
        Assert.True(listed[0].GetProperty("isExpired").GetBoolean());
        var expired = await shop.Business.Client.PostAsJsonAsync($"/api/quotes/{id}/convert", Convert(4000));
        await ApiFactory.Expect(expired, HttpStatusCode.Conflict);
        Assert.Contains("venció", (await ApiFactory.Json(expired)).GetProperty("detail").GetString());

        // editarla le pone vigencia y precios de hoy y ya se puede convertir
        await ApiFactory.Expect(await shop.Business.Client.PutAsJsonAsync($"/api/quotes/{id}", Quote([QuoteLine(shop.Soda, 3)])), HttpStatusCode.OK);
        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/quotes/{id}/convert", Convert(12000)), HttpStatusCode.Created);

        var other = (await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/quotes", Quote([QuoteLine(shop.Soda, 1)]))))
            .GetProperty("id").GetGuid();
        await ApiFactory.Expect(await shop.Business.Client.PostAsync($"/api/quotes/{other}/cancel", null), HttpStatusCode.OK);
        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/quotes/{other}/convert", Convert(4000)), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Quotes_validate_the_customer_name_and_the_validity()
    {
        var shop = await ShopAsync();
        var noName = await shop.Business.Client.PostAsJsonAsync("/api/quotes", Quote([QuoteLine(shop.Soda, 1)], customerName: null));
        await ApiFactory.Expect(noName, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(noName, "customerName"));

        var tooLong = await shop.Business.Client.PostAsJsonAsync("/api/quotes", Quote([QuoteLine(shop.Soda, 1)], validDays: 200));
        await ApiFactory.Expect(tooLong, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(tooLong, "validUntil"));
    }

    [Fact]
    public async Task A_business_cannot_see_or_use_another_business_purchases_and_quotes()
    {
        var shop = await ShopAsync();
        var other = await factory.RegisterBusinessAsync();
        var purchase = (await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/purchases",
            Purchase(shop.Supplier, PurchaseLine(shop.Soda, 1, 2000))))).GetProperty("id").GetGuid();
        var quote = (await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/quotes", Quote([QuoteLine(shop.Soda, 1)]))))
            .GetProperty("id").GetGuid();

        Assert.Equal(0, (await ApiFactory.Json(await other.Client.GetAsync("/api/purchases"))).GetArrayLength());
        Assert.Equal(0, (await ApiFactory.Json(await other.Client.GetAsync("/api/quotes"))).GetArrayLength());
        await ApiFactory.Expect(await other.Client.GetAsync($"/api/purchases/{purchase}"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await other.Client.PostAsJsonAsync($"/api/purchases/{purchase}/void", new { reason = "x" }), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await other.Client.GetAsync($"/api/quotes/{quote}"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await other.Client.PostAsync($"/api/quotes/{quote}/cancel", null), HttpStatusCode.NotFound);

        // el proveedor y el producto de otro negocio no existen para B
        await ApiFactory.Expect(await other.Client.PostAsJsonAsync("/api/purchases",
            Purchase(shop.Supplier, PurchaseLine(shop.Soda, 1, 2000))), HttpStatusCode.BadRequest);
    }
}
