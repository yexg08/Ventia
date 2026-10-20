using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ventia.Tests.Integration;

/// <summary>
/// inventario (el stock nunca queda negativo ni siquiera con salidas simultaneas y siempre es igual a la suma
/// de sus movimientos)
/// </summary>
[Collection(ApiCollection.Name)]
public class StockTests(ApiFactory factory)
{
    private static Task<HttpResponseMessage> Adjust(HttpClient client, Guid productId, string kind, int quantity, string reason = "Prueba") =>
        client.PostAsJsonAsync($"/api/products/{productId}/stock-adjustments", new { kind, quantity, reason });

    private async Task<int> StockAsync(HttpClient client, Guid productId) =>
        (await ApiFactory.Json(await client.GetAsync($"/api/products/{productId}"))).GetProperty("stock").GetInt32();

    [Fact]
    public async Task Entries_exits_and_counts_change_the_stock_and_leave_a_record()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client, initialStock: 10);

        await ApiFactory.Expect(await Adjust(business.Client, productId, "In", 6, "Llegó un pedido"), HttpStatusCode.OK);
        await ApiFactory.Expect(await Adjust(business.Client, productId, "Out", 3, "Se vencieron"), HttpStatusCode.OK);
        var counted = await Adjust(business.Client, productId, "Count", 11, "Conteo del viernes");
        await ApiFactory.Expect(counted, HttpStatusCode.OK);
        Assert.Equal(11, (await ApiFactory.Json(counted)).GetProperty("stock").GetInt32());

        var movements = (await ApiFactory.Json(await business.Client.GetAsync($"/api/products/{productId}/movements")))
            .EnumerateArray().Reverse().ToList();
        Assert.Equal(["Initial", "In", "Out", "Count"], movements.Select(m => m.GetProperty("type").GetString()));
        Assert.Equal([10, 6, -3, -2], movements.Select(m => m.GetProperty("quantity").GetInt32()));
        Assert.Equal([10, 16, 13, 11], movements.Select(m => m.GetProperty("stockAfter").GetInt32()));
        Assert.Equal("Conteo del viernes", movements[3].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_exit_larger_than_the_stock_is_rejected()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client, initialStock: 2);

        var response = await Adjust(business.Client, productId, "Out", 3);
        await ApiFactory.Expect(response, HttpStatusCode.Conflict);
        Assert.Contains("quedan 2", (await ApiFactory.Json(response)).GetProperty("detail").GetString());
        Assert.Equal(2, await StockAsync(business.Client, productId));
    }

    [Fact]
    public async Task Products_without_inventory_cannot_be_adjusted()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client, initialStock: null, tracksStock: false, name: "Hamburguesa");

        var response = await Adjust(business.Client, productId, "In", 5);
        await ApiFactory.Expect(response, HttpStatusCode.Conflict);
        Assert.Contains("no lleva inventario", (await ApiFactory.Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Simultaneous_exits_never_take_more_than_the_available_stock()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client, initialStock: 5);

        // 8 salidas de 1 unidad al mismo tiempo sobre un stock de 5
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Adjust(business.Client, productId, "Out", 1, $"Salida {i}")));

        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await StockAsync(business.Client, productId));
    }

    [Fact]
    public async Task The_stock_always_equals_the_sum_of_its_movements()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client, initialStock: 7);
        await Adjust(business.Client, productId, "In", 4);
        await Adjust(business.Client, productId, "Out", 9);
        await Adjust(business.Client, productId, "Out", 50); // rechazada
        await Adjust(business.Client, productId, "Count", 1);

        var (stock, sum) = await factory.WithDbAsync(business.TenantId, async (db, _) => (
            await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync(),
            await db.StockMovements.Where(m => m.ProductId == productId).SumAsync(m => m.Quantity)));
        Assert.Equal(1, stock);
        Assert.Equal(stock, sum);
    }

    [Fact]
    public async Task The_database_rejects_negative_stock_even_if_the_app_were_bypassed()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client, initialStock: 1);

        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.WithDbAsync(business.TenantId, (db, _) =>
            db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, -1))));
        Assert.Equal("ck_products_stock_non_negative", error.ConstraintName);
    }

    [Fact]
    public async Task Adjustment_requests_are_validated()
    {
        var business = await factory.RegisterBusinessAsync();
        var productId = await ApiFactory.CreateProductAsync(business.Client);

        await ApiFactory.Expect(await Adjust(business.Client, productId, "In", 0), HttpStatusCode.BadRequest);
        await ApiFactory.Expect(await Adjust(business.Client, productId, "Count", -1), HttpStatusCode.BadRequest);
        await ApiFactory.Expect(await Adjust(business.Client, productId, "In", 5, reason: " "), HttpStatusCode.BadRequest);
        await ApiFactory.Expect(await Adjust(business.Client, productId, "Robar", 5), HttpStatusCode.BadRequest);
    }
}
