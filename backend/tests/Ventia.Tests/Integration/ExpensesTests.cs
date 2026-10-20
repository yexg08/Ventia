using System.Net;
using System.Net.Http.Json;

namespace Ventia.Tests.Integration;

/// <summary>gastos y cuentas por pagar (nunca se paga mas que el saldo y las salidas de caja cuadran con el cierre)</summary>
[Collection(ApiCollection.Name)]
public class ExpensesTests(ApiFactory factory)
{
    private sealed record Shop(ApiFactory.Business Business, Guid Soda, Guid Supplier);

    private async Task<Shop> ShopAsync()
    {
        var business = await factory.RegisterBusinessAsync();
        var soda = await ApiFactory.CreateProductAsync(business.Client, initialStock: 0, name: "Gaseosa");
        var created = await business.Client.PostAsJsonAsync("/api/suppliers", new
        {
            documentType = "NIT", documentNumber = "800197268-4", name = "Distribuidora Andina", contactName = (string?)null,
            phone = (string?)null, email = (string?)null, address = (string?)null, notes = (string?)null, isActive = true
        });
        await ApiFactory.Expect(created, HttpStatusCode.Created);
        return new Shop(business, soda, (await ApiFactory.Json(created)).GetProperty("id").GetGuid());
    }

    private static object Purchase(Shop shop, int quantity = 10, decimal unitCost = 2000, string? paidWith = null, bool fromRegister = false) =>
        new
        {
            supplierId = shop.Supplier, supplierInvoice = (string?)null, date = (string?)null, notes = (string?)null,
            lines = new[] { new { productId = shop.Soda, quantity, unitCost } }, paidWith, paidFromCashRegister = fromRegister
        };

    private static async Task<Guid> PurchaseAsync(Shop shop, int quantity = 10, decimal unitCost = 2000, string? paidWith = null, bool fromRegister = false)
    {
        var response = await shop.Business.Client.PostAsJsonAsync("/api/purchases", Purchase(shop, quantity, unitCost, paidWith, fromRegister));
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        return (await ApiFactory.Json(response)).GetProperty("id").GetGuid();
    }

    private static object Pay(decimal amount, string method = "Transfer", bool fromRegister = false) =>
        new { amount, method, date = (string?)null, fromCashRegister = fromRegister, reference = "TRF-1" };

    private static object Expense(decimal amount, string method = "Cash", bool fromRegister = false, string category = "Supplies") =>
        new { date = (string?)null, category, description = "Hielo y servilletas", amount, method, fromCashRegister = fromRegister };

    [Fact]
    public async Task A_credit_purchase_is_payable_until_its_payments_cover_the_total()
    {
        var shop = await ShopAsync();
        var purchase = await PurchaseAsync(shop); // 20.000 a credito

        var payables = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/payables"));
        Assert.Equal(20000m, Assert.Single(payables.EnumerateArray()).GetProperty("balance").GetDecimal());

        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(15000)), HttpStatusCode.Created);
        var over = await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(6000));
        await ApiFactory.Expect(over, HttpStatusCode.Conflict);
        Assert.Contains("5.000", (await ApiFactory.Json(over)).GetProperty("detail").GetString());

        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(5000)), HttpStatusCode.Created);
        var detail = await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/purchases/{purchase}"));
        Assert.Equal(0m, detail.GetProperty("balance").GetDecimal());
        Assert.Equal(2, detail.GetProperty("payments").GetArrayLength());
        Assert.Equal(0, (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/payables"))).GetArrayLength());

        var paidOff = await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(1));
        Assert.Contains("ya está pagada", (await ApiFactory.Json(paidOff)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Simultaneous_payments_never_exceed_the_balance()
    {
        var shop = await ShopAsync();
        var purchase = await PurchaseAsync(shop); // 20.000

        // cinco abonos de 8.000 al mismo tiempo (solo caben dos)
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(8000))));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var detail = await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/purchases/{purchase}"));
        Assert.Equal(16000m, detail.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(4000m, detail.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Voiding_a_payment_restores_the_balance_and_a_paid_purchase_cannot_be_voided()
    {
        var shop = await ShopAsync();
        var purchase = await PurchaseAsync(shop, paidWith: "Transfer");
        var detail = await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/purchases/{purchase}"));
        Assert.Equal(0m, detail.GetProperty("balance").GetDecimal());
        var paymentId = detail.GetProperty("payments")[0].GetProperty("id").GetGuid();

        var blocked = await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/void", new { reason = "Error" });
        await ApiFactory.Expect(blocked, HttpStatusCode.Conflict);
        Assert.Contains("Anula primero sus pagos", (await ApiFactory.Json(blocked)).GetProperty("detail").GetString());

        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/supplier-payments/{paymentId}/void", new { reason = "Se devolvió" }), HttpStatusCode.OK);
        Assert.Equal(20000m, (await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/purchases/{purchase}"))).GetProperty("balance").GetDecimal());
        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/void", new { reason = "Error" }), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Simultaneous_voids_of_a_payment_subtract_it_once()
    {
        var shop = await ShopAsync();
        var purchase = await PurchaseAsync(shop); // 20.000
        Guid first = Guid.Empty;
        for (var i = 0; i < 4; i++)
        {
            var paid = await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(4000)));
            if (i == 0) first = paid.GetProperty("id").GetGuid();
        }

        var voids = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            shop.Business.Client.PostAsJsonAsync($"/api/supplier-payments/{first}/void", new { reason = "Repetido" })));

        Assert.Equal(1, voids.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(12000m, (await ApiFactory.Json(await shop.Business.Client.GetAsync($"/api/purchases/{purchase}"))).GetProperty("paidAmount").GetDecimal());
    }

    [Fact]
    public async Task Cash_taken_from_the_register_reduces_the_expected_cash()
    {
        var shop = await ShopAsync();
        await shop.Business.Client.PostAsJsonAsync("/api/cash-sessions", new { openingCash = 100000 });

        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync("/api/expenses", Expense(12000, fromRegister: true)), HttpStatusCode.Created);
        await PurchaseAsync(shop, quantity: 5, unitCost: 2000, paidWith: "Cash", fromRegister: true); // 10.000
        var credit = await PurchaseAsync(shop); // 20.000 a credito
        await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{credit}/payments", Pay(3000, "Cash", fromRegister: true));
        // por fuera de la caja (no cuentan)
        await shop.Business.Client.PostAsJsonAsync("/api/expenses", Expense(500000, "Transfer", category: "Rent"));
        await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{credit}/payments", Pay(1000));

        var session = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/cash-sessions/current"));
        Assert.Equal(25000m, session.GetProperty("cashOut").GetDecimal());
        Assert.Equal(75000m, session.GetProperty("expectedCash").GetDecimal());

        // un gasto anulado deja de restar
        var expenses = await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/expenses"));
        var fromRegister = expenses.EnumerateArray().First(e => e.GetProperty("fromCashRegister").GetBoolean());
        await shop.Business.Client.PostAsJsonAsync($"/api/expenses/{fromRegister.GetProperty("id").GetGuid()}/void", new { reason = "Error" });
        Assert.Equal(87000m, (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/cash-sessions/current"))).GetProperty("expectedCash").GetDecimal());
    }

    [Fact]
    public async Task Cashiers_only_register_cash_outs_from_their_own_register()
    {
        var shop = await ShopAsync();
        var cashier = (await factory.CreateCashierAsync(shop.Business.Client)).Client;

        var outside = await cashier.PostAsJsonAsync("/api/expenses", Expense(5000, "Transfer"));
        await ApiFactory.Expect(outside, HttpStatusCode.Conflict);
        var noRegister = await cashier.PostAsJsonAsync("/api/expenses", Expense(5000, fromRegister: true));
        await ApiFactory.Expect(noRegister, HttpStatusCode.Conflict);
        Assert.Contains("Abre tu caja", (await ApiFactory.Json(noRegister)).GetProperty("detail").GetString());

        await cashier.PostAsJsonAsync("/api/cash-sessions", new { openingCash = 20000 });
        await ApiFactory.Expect(await cashier.PostAsJsonAsync("/api/expenses", Expense(5000, fromRegister: true)), HttpStatusCode.Created);
        await shop.Business.Client.PostAsJsonAsync("/api/expenses", Expense(9000, "Card"));

        Assert.Equal(1, (await ApiFactory.Json(await cashier.GetAsync("/api/expenses"))).GetArrayLength());
        Assert.Equal(2, (await ApiFactory.Json(await shop.Business.Client.GetAsync("/api/expenses"))).GetArrayLength());

        var id = (await ApiFactory.Json(await cashier.GetAsync("/api/expenses")))[0].GetProperty("id").GetGuid();
        await ApiFactory.Expect(await cashier.PostAsJsonAsync($"/api/expenses/{id}/void", new { reason = "x" }), HttpStatusCode.Forbidden);
        await ApiFactory.Expect(await cashier.GetAsync("/api/payables"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Expense_and_payment_requests_are_validated()
    {
        var shop = await ShopAsync();
        var purchase = await PurchaseAsync(shop);

        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync("/api/expenses", Expense(0)), HttpStatusCode.BadRequest);
        // solo el efectivo puede salir de la caja
        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync("/api/expenses", Expense(1000, "Card", fromRegister: true)), HttpStatusCode.BadRequest);
        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(-1)), HttpStatusCode.BadRequest);
        await ApiFactory.Expect(await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(1000, "Card", fromRegister: true)), HttpStatusCode.BadRequest);
        var future = await shop.Business.Client.PostAsJsonAsync("/api/expenses", new
        {
            date = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"), category = "Other", description = "Futuro", amount = 1000, method = "Cash", fromCashRegister = false
        });
        await ApiFactory.Expect(future, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_business_cannot_see_or_pay_another_business_purchases_or_expenses()
    {
        var shop = await ShopAsync();
        var other = await factory.RegisterBusinessAsync();
        var purchase = await PurchaseAsync(shop);
        await shop.Business.Client.PostAsJsonAsync("/api/expenses", Expense(1000, "Card"));

        Assert.Equal(0, (await ApiFactory.Json(await other.Client.GetAsync("/api/payables"))).GetArrayLength());
        Assert.Equal(0, (await ApiFactory.Json(await other.Client.GetAsync("/api/expenses"))).GetArrayLength());
        await ApiFactory.Expect(await other.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(1000)), HttpStatusCode.NotFound);
        var paymentOfA = await ApiFactory.Json(await shop.Business.Client.PostAsJsonAsync($"/api/purchases/{purchase}/payments", Pay(1000)));
        await ApiFactory.Expect(await other.Client.PostAsJsonAsync($"/api/supplier-payments/{paymentOfA.GetProperty("id").GetGuid()}/void", new { reason = "x" }), HttpStatusCode.NotFound);
    }
}
