using System.Net;
using System.Net.Http.Json;
using Ventia.Domain.Entities;
using Ventia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Tests.Integration;

/// <summary>
/// el corazon del multi negocio (un negocio nunca puede ver ni tocar los datos de otro aunque conozca sus ids)
/// se prueba por la API y tambien directo contra la base (filtro global e interceptor)
/// </summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task A_business_cannot_read_or_change_another_business_categories_by_id()
    {
        var a = await factory.RegisterBusinessAsync("Burger A");
        var b = await factory.RegisterBusinessAsync("Burger B");
        var categoryOfA = await ApiFactory.CreateCategoryAsync(a.Client, "Secreta de A");

        // el negocio B no la ve en su lista
        var list = await ApiFactory.Json(await b.Client.GetAsync("/api/categories"));
        Assert.Equal(0, list.GetArrayLength());

        // y por id recibe 404 igual que con un id inventado (no se revela que existe)
        await ApiFactory.Expect(await b.Client.GetAsync($"/api/categories/{categoryOfA}"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(
            await b.Client.PutAsJsonAsync($"/api/categories/{categoryOfA}", ApiFactory.NewCategory("Hackeada")), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await b.Client.DeleteAsync($"/api/categories/{categoryOfA}"), HttpStatusCode.NotFound);

        // la de A sigue intacta
        var stillThere = await ApiFactory.Json(await a.Client.GetAsync($"/api/categories/{categoryOfA}"));
        Assert.Equal("Secreta de A", stillThere.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Two_businesses_can_use_the_same_category_names()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();
        await ApiFactory.CreateCategoryAsync(a.Client, "Bebidas");
        await ApiFactory.CreateCategoryAsync(b.Client, "Bebidas");
    }

    [Fact]
    public async Task Created_data_always_belongs_to_the_session_business()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();

        // aunque el cuerpo traiga un tenantId ajeno se ignora (el negocio sale de la sesion)
        var response = await b.Client.PostAsJsonAsync("/api/categories", new { tenantId = a.TenantId, name = "Colada", sortOrder = 0, isActive = true });
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        var id = (await ApiFactory.Json(response)).GetProperty("id").GetGuid();

        var owner = await factory.WithDbAsync(null, (db, _) =>
            db.Categories.IgnoreQueryFilters().Where(c => c.Id == id).Select(c => c.TenantId).SingleAsync());
        Assert.Equal(b.TenantId, owner);
    }

    [Fact]
    public async Task Global_filter_only_returns_rows_of_the_current_business()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();
        await ApiFactory.CreateCategoryAsync(a.Client, "De A");
        await ApiFactory.CreateCategoryAsync(b.Client, "De B");

        var seenByA = await factory.WithDbAsync(a.TenantId, (db, _) => db.Categories.Select(c => c.TenantId).Distinct().ToListAsync());
        Assert.Equal(new[] { a.TenantId }, seenByA);

        // sin negocio (superadmin o anonimo) el filtro no deja pasar NADA aunque haya datos
        var seenWithoutTenant = await factory.WithDbAsync(null, (db, _) => db.Categories.CountAsync());
        Assert.Equal(0, seenWithoutTenant);
    }

    [Fact]
    public async Task Interceptor_blocks_writes_on_another_business_data()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();
        var categoryOfA = await ApiFactory.CreateCategoryAsync(a.Client, "Original");

        // aunque un error de codigo cargara el dato saltandose el filtro no se podria guardar
        await Assert.ThrowsAsync<TenantIsolationException>(() => factory.WithDbAsync(b.TenantId, async (db, _) =>
        {
            var category = await db.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == categoryOfA);
            category.Name = "Modificada por B";
            return await db.SaveChangesAsync();
        }));

        await Assert.ThrowsAsync<TenantIsolationException>(() => factory.WithDbAsync(b.TenantId, async (db, _) =>
        {
            var category = await db.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == categoryOfA);
            db.Categories.Remove(category);
            return await db.SaveChangesAsync();
        }));

        await Assert.ThrowsAsync<TenantIsolationException>(() => factory.WithDbAsync(b.TenantId, async (db, _) =>
        {
            db.Categories.Add(new Category { TenantId = a.TenantId, Name = "Para A" });
            return await db.SaveChangesAsync();
        }));

        await Assert.ThrowsAsync<TenantIsolationException>(() => factory.WithDbAsync(null, async (db, _) =>
        {
            db.Categories.Add(new Category { Name = "Sin negocio" });
            return await db.SaveChangesAsync();
        }));

        var name = await factory.WithDbAsync(a.TenantId, (db, _) =>
            db.Categories.Where(c => c.Id == categoryOfA).Select(c => c.Name).SingleAsync());
        Assert.Equal("Original", name);
    }

    [Fact]
    public async Task Business_settings_are_always_the_session_business()
    {
        var a = await factory.RegisterBusinessAsync("Negocio A");
        var b = await factory.RegisterBusinessAsync("Negocio B");

        var settings = await ApiFactory.Json(await b.Client.GetAsync("/api/business"));
        Assert.Equal(b.TenantId, settings.GetProperty("id").GetGuid());

        // el negocio B cambia sus ajustes enviando el id de A (se ignora y solo cambia B)
        var response = await b.Client.PutAsJsonAsync("/api/business", new
        {
            id = a.TenantId, name = "B renombrado", taxId = (string?)null, address = (string?)null, city = (string?)null,
            phone = (string?)null, timeZone = "America/Bogota", defaultTax = "Inc8", receiptFooter = (string?)null
        });
        await ApiFactory.Expect(response, HttpStatusCode.OK);

        var nameOfA = (await ApiFactory.Json(await a.Client.GetAsync("/api/business"))).GetProperty("name").GetString();
        Assert.Equal("Negocio A", nameOfA);
    }
}
