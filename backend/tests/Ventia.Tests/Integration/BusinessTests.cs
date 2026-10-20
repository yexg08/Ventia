using System.Net;
using System.Net.Http.Json;

namespace Ventia.Tests.Integration;

/// <summary>ajustes del negocio y categorias del menu</summary>
[Collection(ApiCollection.Name)]
public class BusinessTests(ApiFactory factory)
{
    private static object Settings(string? taxId = null, string? phone = null, string defaultTax = "Iva19") => new
    {
        name = "Perros El Parque", taxId, address = "Cra 23 # 65-10", city = "Manizales", phone,
        timeZone = "America/Bogota", defaultTax, receiptFooter = "¡Gracias por tu compra!"
    };

    [Fact]
    public async Task Owner_updates_the_settings_and_the_nit_is_normalized()
    {
        var business = await factory.RegisterBusinessAsync();

        var response = await business.Client.PutAsJsonAsync("/api/business", Settings("900.123.456-8", "+57 300 123 4567"));
        await ApiFactory.Expect(response, HttpStatusCode.OK);

        var saved = await ApiFactory.Json(await business.Client.GetAsync("/api/business"));
        Assert.Equal("900123456-8", saved.GetProperty("taxId").GetString());
        Assert.Equal("573001234567", saved.GetProperty("phone").GetString());
        Assert.Equal("Iva19", saved.GetProperty("defaultTax").GetString());
        Assert.Equal("Manizales", saved.GetProperty("city").GetString());
    }

    [Theory]
    [InlineData("900123456-7", "taxId")]
    [InlineData("900123456", "taxId")]
    [InlineData("abc-1", "taxId")]
    public async Task Invalid_nits_are_rejected(string taxId, string field)
    {
        var business = await factory.RegisterBusinessAsync();
        var response = await business.Client.PutAsJsonAsync("/api/business", Settings(taxId));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(response, field));
    }

    [Fact]
    public async Task Unknown_tax_types_are_rejected()
    {
        var business = await factory.RegisterBusinessAsync();
        var response = await business.Client.PutAsJsonAsync("/api/business", Settings(defaultTax: "Iva50"));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cashier_cannot_change_the_settings()
    {
        var business = await factory.RegisterBusinessAsync();
        var cashier = await factory.CreateCashierAsync(business.Client);
        await ApiFactory.Expect(await cashier.Client.PutAsJsonAsync("/api/business", Settings()), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Categories_are_listed_in_order_and_names_are_unique_ignoring_case()
    {
        var business = await factory.RegisterBusinessAsync();
        await business.Client.PostAsJsonAsync("/api/categories", ApiFactory.NewCategory("Bebidas", 20));
        await business.Client.PostAsJsonAsync("/api/categories", ApiFactory.NewCategory("Hamburguesas", 10));
        await business.Client.PostAsJsonAsync("/api/categories", ApiFactory.NewCategory("Adiciones", 20));

        var names = (await ApiFactory.Json(await business.Client.GetAsync("/api/categories")))
            .EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Hamburguesas", "Adiciones", "Bebidas"], names);

        var duplicated = await business.Client.PostAsJsonAsync("/api/categories", ApiFactory.NewCategory("  bebidas "));
        await ApiFactory.Expect(duplicated, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(duplicated, "name"));
    }

    [Fact]
    public async Task A_category_can_be_renamed_deactivated_and_deleted()
    {
        var business = await factory.RegisterBusinessAsync();
        var id = await ApiFactory.CreateCategoryAsync(business.Client, "Perros");

        var updated = await business.Client.PutAsJsonAsync($"/api/categories/{id}", ApiFactory.NewCategory("Perros calientes", 5, isActive: false));
        await ApiFactory.Expect(updated, HttpStatusCode.OK);
        var body = await ApiFactory.Json(updated);
        Assert.Equal("Perros calientes", body.GetProperty("name").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());

        // renombrarla con su mismo nombre no choca consigo misma
        await ApiFactory.Expect(await business.Client.PutAsJsonAsync($"/api/categories/{id}", ApiFactory.NewCategory("PERROS CALIENTES")),
            HttpStatusCode.OK);

        await ApiFactory.Expect(await business.Client.DeleteAsync($"/api/categories/{id}"), HttpStatusCode.NoContent);
        await ApiFactory.Expect(await business.Client.GetAsync($"/api/categories/{id}"), HttpStatusCode.NotFound);
    }
}
