using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ventia.Tests.Integration;

/// <summary>
/// punto de venta (el servidor pone precios e impuestos y la venta es todo o nada y el consecutivo no tiene saltos
/// ni repetidos y el mismo cobro dos veces es una sola venta)
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesTests(ApiFactory factory)
{
    private sealed record Shop(ApiFactory.Business Business, Guid Burger, Guid Soda);

    /// <summary>negocio con caja abierta y dos productos (hamburguesa sin inventario y gaseosa con 10 unidades)</summary>
    private async Task<Shop> ShopAsync()
    {
        var business = await factory.RegisterBusinessAsync();
        var food = await ApiFactory.CreateCategoryAsync(business.Client, "Hamburguesas");
        var drinks = await ApiFactory.CreateCategoryAsync(business.Client, "Bebidas");
        var burger = await ProductAsync(business.Client, ApiFactory.NewProduct(food, "Hamburguesa sencilla", price: 18000, tracksStock: false, initialStock: null));
        var soda = await ProductAsync(business.Client, ApiFactory.NewProduct(drinks, "Gaseosa", price: 4000, initialStock: 10));
        await OpenCashAsync(business.Client);
        return new Shop(business, burger, soda);
    }

    private static async Task<Guid> ProductAsync(HttpClient client, object product)
    {
        var response = await client.PostAsJsonAsync("/api/products", product);
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        return (await ApiFactory.Json(response)).GetProperty("id").GetGuid();
    }

    private static async Task OpenCashAsync(HttpClient client, decimal openingCash = 50000) =>
        await ApiFactory.Expect(await client.PostAsJsonAsync("/api/cash-sessions", new { openingCash }), HttpStatusCode.Created);

    private static object Line(Guid productId, int quantity = 1, decimal discountPercent = 0, string? note = null, int? parentIndex = null) =>
        new { productId, quantity, discountPercent, note, parentIndex };

    private static object Pay(string method, decimal amount) => new { method, amount };

    private static object Sale(object[] lines, object[] payments, decimal cashTendered = 0, Guid? key = null,
        string orderType = "TakeAway", string? deliveryAddress = null, Guid? customerId = null) =>
        new
        {
            idempotencyKey = key ?? Guid.NewGuid(), orderType, tableLabel = (string?)null, deliveryAddress, customerId,
            lines, payments, cashTendered, notes = (string?)null
        };

    private static Task<HttpResponseMessage> SellAsync(HttpClient client, object sale) => client.PostAsJsonAsync("/api/sales", sale);

    private static async Task<int> StockAsync(HttpClient client, Guid productId) =>
        (await ApiFactory.Json(await client.GetAsync($"/api/products/{productId}"))).GetProperty("stock").GetInt32();

    [Fact]
    public async Task The_server_computes_prices_taxes_discounts_and_change()
    {
        var shop = await ShopAsync();

        // 2 hamburguesas con 10 por ciento de descuento y 3 gaseosas (el navegador manda un precio falso que se ignora)
        var request = new
        {
            idempotencyKey = Guid.NewGuid(), orderType = "DineIn", tableLabel = "Mesa 4", deliveryAddress = (string?)null,
            customerId = (Guid?)null, notes = (string?)null, cashTendered = 50000m,
            lines = new object[]
            {
                new { productId = shop.Burger, quantity = 2, discountPercent = 10m, note = "sin cebolla", parentIndex = (int?)null, price = 1m },
                Line(shop.Soda, 3)
            },
            payments = new[] { Pay("Cash", 44400) }
        };
        var response = await SellAsync(shop.Business.Client, request);
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        var sale = await ApiFactory.Json(response);

        Assert.Equal(1, sale.GetProperty("number").GetInt32());
        Assert.Equal(44400m, sale.GetProperty("total").GetDecimal());
        Assert.Equal(3600m, sale.GetProperty("discountTotal").GetDecimal());
        Assert.Equal(sale.GetProperty("total").GetDecimal(), sale.GetProperty("subtotal").GetDecimal() + sale.GetProperty("taxTotal").GetDecimal());
        Assert.Equal(5600m, sale.GetProperty("change").GetDecimal());
        Assert.Equal("Consumidor final", sale.GetProperty("customerName").GetString());
        Assert.Equal("Mesa 4", sale.GetProperty("tableLabel").GetString());

        var burgerLine = sale.GetProperty("lines")[0];
        Assert.Equal(18000m, burgerLine.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(32400m, burgerLine.GetProperty("lineTotal").GetDecimal());
        Assert.Equal("sin cebolla", burgerLine.GetProperty("note").GetString());

        // solo la gaseosa lleva inventario y su movimiento apunta a la venta
        Assert.Equal(7, await StockAsync(shop.Business.Client, shop.Soda));
        var movements = await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/products/{shop.Soda}/movements"));
        Assert.Equal("Sale", movements[0].GetProperty("type").GetString());
        Assert.Equal("Venta 1", movements[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_sale_is_all_or_nothing_and_a_failed_sale_does_not_consume_a_number()
    {
        var shop = await ShopAsync();

        // pagos que no cuadran con el total
        var wrongPayment = await SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Card", 1000)]));
        await ApiFactory.Expect(wrongPayment, HttpStatusCode.Conflict);
        Assert.Contains("total es 18.000", (await ApiFactory.Json(wrongPayment)).GetProperty("detail").GetString());

        // no hay suficiente gaseosa (la hamburguesa tampoco queda vendida)
        var noStock = await SellAsync(shop.Business.Client, Sale([Line(shop.Burger), Line(shop.Soda, 11)], [Pay("Card", 62000)]));
        await ApiFactory.Expect(noStock, HttpStatusCode.Conflict);
        Assert.Contains("quedan 10", (await ApiFactory.Json(noStock)).GetProperty("detail").GetString());
        Assert.Equal(10, await StockAsync(shop.Business.Client, shop.Soda));

        var ok = await SellAsync(shop.Business.Client, Sale([Line(shop.Soda)], [Pay("Transfer", 4000)]));
        await ApiFactory.Expect(ok, HttpStatusCode.Created);
        Assert.Equal(1, (await ApiFactory.Json(ok)).GetProperty("number").GetInt32());

        var list = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/sales"));
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task Simultaneous_sales_get_consecutive_numbers_without_gaps_or_repeats()
    {
        var shop = await ShopAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Card", 18000)]))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = new List<int>();
        foreach (var r in responses) numbers.Add((await ApiFactory.Json(r)).GetProperty("number").GetInt32());
        Assert.Equal(Enumerable.Range(1, 10), numbers.Order());
    }

    [Fact]
    public async Task Charging_twice_with_the_same_key_creates_a_single_sale()
    {
        var shop = await ShopAsync();
        var key = Guid.NewGuid();
        var request = Sale([Line(shop.Soda, 2)], [Pay("Cash", 8000)], key: key);

        // doble clic seguido y luego cinco reintentos al mismo tiempo
        var first = await ApiFactory.Json(await SellAsync(shop.Business.Client, request));
        var second = await ApiFactory.Json(await SellAsync(shop.Business.Client, request));
        var burst = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SellAsync(shop.Business.Client, request)));

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        foreach (var r in burst)
            Assert.Equal(first.GetProperty("id").GetGuid(), (await ApiFactory.Json(r)).GetProperty("id").GetGuid());

        Assert.Equal(1, (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/sales"))).GetArrayLength());
        Assert.Equal(8, await StockAsync(shop.Business.Client, shop.Soda));
    }

    [Fact]
    public async Task Simultaneous_sales_never_sell_more_stock_than_available()
    {
        var shop = await ShopAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            SellAsync(shop.Business.Client, Sale([Line(shop.Soda, 2)], [Pay("Card", 8000)]))));

        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await StockAsync(shop.Business.Client, shop.Soda));

        // los numeros siguen sin saltos aunque hubo ventas rechazadas
        var numbers = (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/sales")))
            .EnumerateArray().Select(s => s.GetProperty("number").GetInt32()).Order();
        Assert.Equal(Enumerable.Range(1, 5), numbers);
    }

    [Fact]
    public async Task Mixed_payments_and_add_ons_are_supported()
    {
        var shop = await ShopAsync();
        var food = await ApiFactory.CreateCategoryAsync(shop.Business.Client, "Adiciones");
        var cheese = await ProductAsync(shop.Business.Client, ApiFactory.NewProduct(food, "Queso extra", price: 3000, tracksStock: false, initialStock: null));

        var response = await SellAsync(shop.Business.Client,
            Sale([Line(shop.Burger), Line(cheese, parentIndex: 0)], [Pay("Card", 15000), Pay("Cash", 6000)], cashTendered: 10000));
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        var sale = await ApiFactory.Json(response);

        Assert.Equal(21000m, sale.GetProperty("total").GetDecimal());
        Assert.Equal(4000m, sale.GetProperty("change").GetDecimal());
        var lines = sale.GetProperty("lines");
        Assert.Equal(lines[0].GetProperty("id").GetGuid(), lines[1].GetProperty("parentLineId").GetGuid());

        // una adicion no puede colgar de otra adicion
        var invalid = await SellAsync(shop.Business.Client,
            Sale([Line(shop.Burger), Line(cheese, parentIndex: 0), Line(cheese, parentIndex: 1)], [Pay("Card", 24000)]));
        await ApiFactory.Expect(invalid, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Voiding_restores_the_stock_and_only_the_owner_can_do_it()
    {
        var shop = await ShopAsync();
        var sale = await ApiFactory.Json(await SellAsync(shop.Business.Client, Sale([Line(shop.Soda, 4)], [Pay("Cash", 16000)])));
        var id = sale.GetProperty("id").GetGuid();
        Assert.Equal(6, await StockAsync(shop.Business.Client, shop.Soda));

        var cashier = (await factory.CreateCashierAsync(shop.Business.Client)).Client;
        await ApiFactory.Expect(await cashier.PostAsJsonAsync($"/api/sales/{id}/void", new { reason = "Error" }), HttpStatusCode.Forbidden);

        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/sales/{id}/void", new { reason = " " }), HttpStatusCode.BadRequest);
        var voided = await shop.Business.Client.PostAsJsonAsync($"/api/sales/{id}/void", new { reason = "El cliente se arrepintió" });
        await ApiFactory.Expect(voided, HttpStatusCode.OK);
        Assert.Equal("Voided", (await ApiFactory.Json(voided)).GetProperty("status").GetString());
        Assert.Equal(10, await StockAsync(shop.Business.Client, shop.Soda));

        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/sales/{id}/void", new { reason = "Otra vez" }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_cash_session_expects_the_opening_plus_cash_of_non_voided_sales()
    {
        var shop = await ShopAsync(); // base de 50.000
        await SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Cash", 18000)], cashTendered: 20000));
        await SellAsync(shop.Business.Client, Sale([Line(shop.Soda)], [Pay("Card", 4000)]));
        var toVoid = await ApiFactory.Json(await SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Cash", 18000)])));
        await shop.Business.Client.PostAsJsonAsync($"/api/sales/{toVoid.GetProperty("id").GetGuid()}/void", new { reason = "Prueba" });

        var current = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/cash-sessions/current"));
        Assert.Equal(68000m, current.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(2, current.GetProperty("salesCount").GetInt32());
        Assert.Equal(1, current.GetProperty("voidedCount").GetInt32());

        var closed = await shop.Business.Client.PostAsJsonAsync("/api/cash-sessions/current/close", new { countedCash = 67000m, notes = "Faltó un billete" });
        await ApiFactory.Expect(closed, HttpStatusCode.OK);
        var session = await ApiFactory.Json(closed);
        Assert.Equal(-1000m, session.GetProperty("difference").GetDecimal());

        // sin caja abierta no se vende
        await ApiFactory.Expect(await shop.Business.Client.GetAsync("/api/cash-sessions/current"), HttpStatusCode.NoContent);
        var noSession = await SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Card", 18000)]));
        await ApiFactory.Expect(noSession, HttpStatusCode.Conflict);
        Assert.Contains("Abre tu caja", (await ApiFactory.Json(noSession)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_account_cannot_have_two_open_cash_sessions_even_at_the_same_time()
    {
        var business = await factory.RegisterBusinessAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            business.Client.PostAsJsonAsync("/api/cash-sessions", new { openingCash = 0 })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Cashiers_only_see_their_own_sales_and_the_owner_sees_all()
    {
        var shop = await ShopAsync();
        var cashierA = await factory.CreateCashierAsync(shop.Business.Client);
        var cashierB = await factory.CreateCashierAsync(shop.Business.Client);
        await OpenCashAsync(cashierA.Client);
        await OpenCashAsync(cashierB.Client);

        var saleOfA = await ApiFactory.Json(await SellAsync(cashierA.Client, Sale([Line(shop.Burger)], [Pay("Card", 18000)])));
        await SellAsync(cashierB.Client, Sale([Line(shop.Burger)], [Pay("Card", 18000)]));
        Assert.Equal("Cajero de prueba", saleOfA.GetProperty("sellerName").GetString());

        Assert.Equal(1, (await ApiFactory.Json(await cashierA.Client.GetAsync("/api/sales"))).GetArrayLength());
        Assert.Equal(2, (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/sales"))).GetArrayLength());
        await ApiFactory.Expect(await cashierB.Client.GetAsync($"/api/sales/{saleOfA.GetProperty("id").GetGuid()}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Inactive_or_foreign_products_cannot_be_sold()
    {
        var shop = await ShopAsync();
        var other = await factory.RegisterBusinessAsync();
        var foreignProduct = await ApiFactory.CreateProductAsync(other.Client, name: "De otro negocio");
        var categoryId = await ApiFactory.CreateCategoryAsync(shop.Business.Client, "Retirados");
        var inactive = await ProductAsync(shop.Business.Client,
            ApiFactory.NewProduct(categoryId, "Malteada", price: 9000, tracksStock: false, initialStock: null, isActive: false));

        var foreign = await SellAsync(shop.Business.Client, Sale([Line(foreignProduct)], [Pay("Card", 4000)]));
        await ApiFactory.Expect(foreign, HttpStatusCode.Conflict);
        var off = await SellAsync(shop.Business.Client, Sale([Line(inactive)], [Pay("Card", 9000)]));
        await ApiFactory.Expect(off, HttpStatusCode.Conflict);
        Assert.Contains("inactivo", (await ApiFactory.Json(off)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Delivery_orders_need_an_address()
    {
        var shop = await ShopAsync();
        var response = await SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Card", 18000)], orderType: "Delivery"));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(response, "deliveryAddress"));
    }

    [Fact]
    public async Task Deleting_a_customer_with_sales_anonymizes_it_and_its_receipts()
    {
        var shop = await ShopAsync();
        var customer = await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync("/api/customers", new
        {
            documentType = "CC", documentNumber = "1053123456", name = "Camila Ríos", phone = "3001234567",
            email = (string?)null, address = (string?)null, notes = (string?)null, dataConsent = true
        }));
        var customerId = customer.GetProperty("id").GetGuid();
        var sale = await ApiFactory.Json(await SellAsync(shop.Business.Client,
            Sale([Line(shop.Burger)], [Pay("Card", 18000)], customerId: customerId)));
        Assert.Equal("Camila Ríos", sale.GetProperty("customerName").GetString());
        Assert.Equal("CC 1053123456", sale.GetProperty("customerDocument").GetString());

        await ApiFactory.Expect(await shop.Business.Client.DeleteAsync($"/api/customers/{customerId}"), HttpStatusCode.NoContent);

        var receipt = await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/sales/{sale.GetProperty("id").GetGuid()}"));
        Assert.Equal("Cliente eliminado", receipt.GetProperty("customerName").GetString());
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("customerDocument").ValueKind);
        Assert.Equal(0, (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/customers"))).GetArrayLength());
        await ApiFactory.Expect(await shop.Business.Client.GetAsync($"/api/customers/{customerId}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sold_products_cannot_be_deleted_even_without_inventory()
    {
        var shop = await ShopAsync();
        await SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Card", 18000)]));
        await ApiFactory.Expect(await shop.Business.Client.DeleteAsync($"/api/products/{shop.Burger}"), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_business_cannot_see_or_void_another_business_sales()
    {
        var shop = await ShopAsync();
        var other = await factory.RegisterBusinessAsync();
        var sale = await ApiFactory.Json(await SellAsync(shop.Business.Client, Sale([Line(shop.Burger)], [Pay("Card", 18000)])));
        var id = sale.GetProperty("id").GetGuid();

        Assert.Equal(0, (await ApiFactory.Json(await other.Client.GetAsync("/api/sales"))).GetArrayLength());
        await ApiFactory.Expect(await other.Client.GetAsync($"/api/sales/{id}"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await other.Client.PostAsJsonAsync($"/api/sales/{id}/void", new { reason = "Ajena" }), HttpStatusCode.NotFound);
    }
}
