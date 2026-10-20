using System.Net;
using System.Net.Http.Json;

namespace Ventia.Tests.Integration;

/// <summary>superadmin (suspender negocios y listado) y cabeceras de seguridad</summary>
[Collection(ApiCollection.Name)]
public class PlatformTests(ApiFactory factory)
{
    [Fact]
    public async Task Suspending_a_business_cuts_the_sessions_of_all_its_accounts()
    {
        var business = await factory.RegisterBusinessAsync();
        var cashier = await factory.CreateCashierAsync(business.Client);
        var admin = await factory.LoginAsSuperAdminAsync();

        await ApiFactory.Expect(await admin.PatchAsJsonAsync($"/api/admin/tenants/{business.TenantId}/status", new { isActive = false }),
            HttpStatusCode.NoContent);

        // las sesiones abiertas dejan de servir de inmediato sin esperar a que venza el token
        await ApiFactory.Expect(await business.Client.GetAsync("/api/business"), HttpStatusCode.Unauthorized);
        await ApiFactory.Expect(await cashier.Client.GetAsync("/api/categories"), HttpStatusCode.Unauthorized);
        await ApiFactory.Expect(await business.Client.PostAsync("/api/auth/refresh", null), HttpStatusCode.Unauthorized);

        var login = await factory.NewClient().PostAsJsonAsync("/api/auth/login", new { email = business.Email, password = ApiFactory.OwnerPassword });
        await ApiFactory.Expect(login, HttpStatusCode.Unauthorized);
        Assert.Contains("suspendido", (await ApiFactory.Json(login)).GetProperty("detail").GetString());

        // al reactivarlo todo vuelve
        await ApiFactory.Expect(await admin.PatchAsJsonAsync($"/api/admin/tenants/{business.TenantId}/status", new { isActive = true }),
            HttpStatusCode.NoContent);
        await factory.LoginAsync(business.Email, ApiFactory.OwnerPassword);
        await factory.LoginAsync(cashier.Email, ApiFactory.CashierPassword);
    }

    [Fact]
    public async Task Superadmin_lists_businesses_with_owner_and_search()
    {
        var business = await factory.RegisterBusinessAsync($"Perros Zafiro {Guid.NewGuid():N}"[..30]);
        await factory.CreateCashierAsync(business.Client);
        var admin = await factory.LoginAsSuperAdminAsync();

        // se puede buscar por el correo del dueno
        var list = await ApiFactory.Json(await admin.GetAsync($"/api/admin/tenants?search={business.Email}"));
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal(business.Email, row.GetProperty("ownerEmail").GetString());
        Assert.Equal(2, row.GetProperty("userCount").GetInt32());

        // los comodines de LIKE se tratan como texto y no como todo
        var wildcard = await ApiFactory.Json(await admin.GetAsync("/api/admin/tenants?search=%25%25%25"));
        Assert.Equal(0, wildcard.GetArrayLength());

        var stats = await ApiFactory.Json(await admin.GetAsync("/api/admin/stats"));
        Assert.True(stats.GetProperty("businesses").GetInt32() >= 1);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await factory.NewClient().GetAsync("/api/business");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
