using System.Net;
using System.Net.Http.Json;

namespace Ventia.Tests.Integration;

/// <summary>catalogo (precios con impuesto incluido y costos que solo ve el dueno y codigos unicos por negocio)</summary>
[Collection(ApiCollection.Name)]
public class ProductsTests(ApiFactory factory)
{
    [Fact]
    public async Task Owner_creates_a_product_with_tax_breakdown_margin_and_initial_stock()
    {
        var business = await factory.RegisterBusinessAsync();
        var categoryId = await ApiFactory.CreateCategoryAsync(business.Client, "Bebidas");

        var response = await business.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(categoryId, "Gaseosa 400 ml", price: 4000, cost: 2200, tax: "Inc8", initialStock: 24, code: "gas-400"));
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        var product = await ApiFactory.Json(response);

        Assert.Equal("Bebidas", product.GetProperty("categoryName").GetString());
        Assert.Equal("GAS-400", product.GetProperty("code").GetString());
        // 4000 con INC del 8 por ciento son 3703.70 de base y 296.30 de impuesto
        Assert.Equal(3703.70m, product.GetProperty("basePrice").GetDecimal());
        Assert.Equal(296.30m, product.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(2200m, product.GetProperty("cost").GetDecimal());
        Assert.Equal(40.6m, product.GetProperty("marginPercent").GetDecimal());
        Assert.Equal(24, product.GetProperty("stock").GetInt32());

        var id = product.GetProperty("id").GetGuid();
        var movements = await ApiFactory.Json(await business.Client.GetAsync($"/api/products/{id}/movements"));
        var initial = Assert.Single(movements.EnumerateArray());
        Assert.Equal("Initial", initial.GetProperty("type").GetString());
        Assert.Equal(24, initial.GetProperty("quantity").GetInt32());
        Assert.Equal("Dueña de prueba", initial.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Cashier_sees_products_without_costs_and_cannot_manage_them()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client);
        var cashier = (await factory.CreateCashierAsync(business.Client)).Client;

        var product = await ApiFactory.Json(await cashier.GetAsync($"/api/products/{productId}"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, product.GetProperty("cost").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, product.GetProperty("marginPercent").ValueKind);
        Assert.Equal(10, product.GetProperty("stock").GetInt32());

        var list = await cashier.GetAsync("/api/products");
        Assert.DoesNotContain("2200", await list.Content.ReadAsStringAsync());

        await ApiFactory.Expect(await cashier.GetAsync($"/api/products/{productId}/movements"), HttpStatusCode.Forbidden);
        await ApiFactory.Expect(await cashier.PostAsJsonAsync($"/api/products/{productId}/stock-adjustments",
            new { kind = "In", quantity = 5, reason = "Intento" }), HttpStatusCode.Forbidden);
        await ApiFactory.Expect(await cashier.DeleteAsync($"/api/products/{productId}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Codes_and_barcodes_are_unique_per_business_but_can_repeat_across_businesses()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();
        var categoryA = await ApiFactory.CreateCategoryAsync(a.Client);
        var categoryB = await ApiFactory.CreateCategoryAsync(b.Client);

        await ApiFactory.Expect(await a.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(categoryA, "Perro sencillo", code: "PS1", barcode: "7701234567890")), HttpStatusCode.Created);

        var sameCode = await a.Client.PostAsJsonAsync("/api/products", ApiFactory.NewProduct(categoryA, "Otro", code: "ps1"));
        await ApiFactory.Expect(sameCode, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(sameCode, "code"));

        var sameBarcode = await a.Client.PostAsJsonAsync("/api/products", ApiFactory.NewProduct(categoryA, "Otro", barcode: "7701234567890"));
        await ApiFactory.Expect(sameBarcode, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(sameBarcode, "barcode"));

        await ApiFactory.Expect(await b.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(categoryB, "Perro de B", code: "PS1", barcode: "7701234567890")), HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_product_cannot_use_a_category_of_another_business()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();
        var categoryOfA = await ApiFactory.CreateCategoryAsync(a.Client);

        var response = await b.Client.PostAsJsonAsync("/api/products", ApiFactory.NewProduct(categoryOfA));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(response, "categoryId"));
    }

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(1000.555, null)]
    [InlineData(1000.0, -5.0)]
    public async Task Invalid_prices_and_costs_are_rejected(double price, double? cost)
    {
        var business = await factory.RegisterBusinessAsync();
        var categoryId = await ApiFactory.CreateCategoryAsync(business.Client);
        var response = await business.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(categoryId, price: (decimal)price, cost: (decimal?)cost));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_filters_by_search_category_and_low_stock()
    {
        var business = await factory.RegisterBusinessAsync();
        var drinks = await ApiFactory.CreateCategoryAsync(business.Client, "Bebidas");
        var burgers = await ApiFactory.CreateCategoryAsync(business.Client, "Hamburguesas");
        await business.Client.PostAsJsonAsync("/api/products", ApiFactory.NewProduct(drinks, "Agua", minStock: 5, initialStock: 3));
        await business.Client.PostAsJsonAsync("/api/products", ApiFactory.NewProduct(drinks, "Jugo de mora", minStock: 5, initialStock: 20));
        await business.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(burgers, "Hamburguesa sencilla", price: 18000, tracksStock: false, initialStock: null));

        async Task<List<string?>> Names(string query) =>
            (await ApiFactory.Json(await business.Client.GetAsync($"/api/products{query}")))
                .EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();

        Assert.Equal(["Agua", "Jugo de mora", "Hamburguesa sencilla"], await Names(""));
        Assert.Equal(["Jugo de mora"], await Names("?search=MORA"));
        Assert.Equal(["Hamburguesa sencilla"], await Names($"?categoryId={burgers}"));
        // solo los que llevan inventario pueden salir con stock bajo
        Assert.Equal(["Agua"], await Names("?lowStock=true"));
    }

    [Fact]
    public async Task Products_without_movements_can_be_deleted_but_with_movements_only_deactivated()
    {
        var business = await factory.RegisterBusinessAsync();
        var fresh = await ApiFactory.CreateProductAsync(business.Client, name: "Recién creado");
        await ApiFactory.Expect(await business.Client.DeleteAsync($"/api/products/{fresh}"), HttpStatusCode.NoContent);

        var used = await ApiFactory.CreateProductAsync(business.Client, name: "Con movimientos");
        await business.Client.PostAsJsonAsync($"/api/products/{used}/stock-adjustments", new { kind = "Out", quantity = 1, reason = "Se dañó" });
        var response = await business.Client.DeleteAsync($"/api/products/{used}");
        await ApiFactory.Expect(response, HttpStatusCode.Conflict);
        Assert.Contains("Desactívalo", (await ApiFactory.Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_category_with_products_cannot_be_deleted()
    {
        var business = await factory.RegisterBusinessAsync();
        var categoryId = await ApiFactory.CreateCategoryAsync(business.Client, "Perros");
        await business.Client.PostAsJsonAsync("/api/products", ApiFactory.NewProduct(categoryId, "Perro especial"));

        var response = await business.Client.DeleteAsync($"/api/categories/{categoryId}");
        await ApiFactory.Expect(response, HttpStatusCode.Conflict);
        Assert.Contains("1 producto", (await ApiFactory.Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_product_stops_tracking_stock_only_when_its_stock_is_zero()
    {
        var business = await factory.RegisterBusinessAsync();
        var categoryId = await ApiFactory.CreateCategoryAsync(business.Client);
        var created = await ApiFactory.Json(await business.Client.PostAsJsonAsync("/api/products",
            ApiFactory.NewProduct(categoryId, "Malteada", initialStock: 2)));
        var id = created.GetProperty("id").GetGuid();

        var blocked = await business.Client.PutAsJsonAsync($"/api/products/{id}",
            ApiFactory.NewProduct(categoryId, "Malteada", tracksStock: false));
        await ApiFactory.Expect(blocked, HttpStatusCode.Conflict);

        await business.Client.PostAsJsonAsync($"/api/products/{id}/stock-adjustments", new { kind = "Count", quantity = 0, reason = "Conteo" });
        await ApiFactory.Expect(await business.Client.PutAsJsonAsync($"/api/products/{id}",
            ApiFactory.NewProduct(categoryId, "Malteada", tracksStock: false)), HttpStatusCode.OK);
    }
}
