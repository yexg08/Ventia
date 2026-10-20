using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ventia.Application.Abstractions;
using Ventia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ventia.Tests.Integration;

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}

/// <summary>
/// arranca la API completa (el Program.cs real) contra un PostgreSQL de verdad en Docker (Testcontainers) con las
/// migraciones reales
/// una sola base para todas las pruebas (cada prueba crea sus propios negocios con datos unicos)
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SuperAdminEmail = "superadmin@ventia.test";
    public const string SuperAdminPassword = "SuperAdmin2026";
    public const string OwnerPassword = "Negocio2026";
    public const string CashierPassword = "Cajero2026x";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("ventia_tests")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Server; // arranca la API ahora (aplica las migraciones y crea el superadmin)
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Key", new string('k', 64));
        builder.UseSetting("Seed:SuperAdminEmail", SuperAdminEmail);
        builder.UseSetting("Seed:SuperAdminPassword", SuperAdminPassword);
        builder.UseSetting("RateLimiting:AuthPerMinute", "100000");
    }

    /// <summary>cliente por https (la cookie de refresh es Secure y no viaja por http)</summary>
    public HttpClient NewClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    public sealed record Business(HttpClient Client, Guid TenantId, string Email);

    /// <summary>registra un negocio nuevo (datos unicos) y devuelve su cliente HTTP con la sesion del dueno</summary>
    public async Task<Business> RegisterBusinessAsync(string name = "Negocio de prueba")
    {
        var email = $"dueno.{Guid.NewGuid():N}@ventia.test";
        var client = NewClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { businessName = name, fullName = "Dueña de prueba", email, password = OwnerPassword });
        await Expect(response, HttpStatusCode.Created);

        var body = await Json(response);
        Authorize(client, body);
        var tenantId = body.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        return new Business(client, tenantId, email);
    }

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        await Expect(response, HttpStatusCode.OK);
        Authorize(client, await Json(response));
        return client;
    }

    public Task<HttpClient> LoginAsSuperAdminAsync() => LoginAsync(SuperAdminEmail, SuperAdminPassword);

    public sealed record Cashier(HttpClient Client, Guid UserId, string Email);

    /// <summary>
    /// el dueno crea un cajero por la API y el cajero entra con la contrasena temporal y la cambia
    /// devuelve el cliente del cajero ya listo para trabajar
    /// </summary>
    public async Task<Cashier> CreateCashierAsync(HttpClient owner)
    {
        var email = $"cajero.{Guid.NewGuid():N}@ventia.test";
        var created = await owner.PostAsJsonAsync("/api/users", new { fullName = "Cajero de prueba", email });
        await Expect(created, HttpStatusCode.Created);
        var body = await Json(created);
        var temporary = body.GetProperty("temporaryPassword").GetString()!;

        var client = await LoginAsync(email, temporary);
        var changed = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = temporary, newPassword = CashierPassword });
        await Expect(changed, HttpStatusCode.OK);
        Authorize(client, await Json(changed));

        return new Cashier(client, body.GetProperty("userId").GetGuid(), email);
    }

    /// <summary>ejecuta codigo contra la base con un negocio fijado a mano (como lo haria un proceso sin peticion)</summary>
    public async Task<T> WithDbAsync<T>(Guid? tenantId, Func<AppDbContext, IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider);
    }

    public static void Authorize(HttpClient client, JsonElement authBody) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authBody.GetProperty("accessToken").GetString());

    public static async Task Expect(HttpResponseMessage response, HttpStatusCode status)
    {
        if (response.StatusCode == status) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new Xunit.Sdk.XunitException(
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: se esperaba {(int)status} " +
            $"y llegó {(int)response.StatusCode}. Cuerpo: {body}");
    }

    public static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<bool> HasFieldError(HttpResponseMessage response, string field) =>
        (await Json(response)).GetProperty("errors").TryGetProperty(field, out _);

    public static object NewCategory(string name = "Hamburguesas", int sortOrder = 0, bool isActive = true) =>
        new { name, sortOrder, isActive };

    public static object NewProduct(
        Guid categoryId, string name = "Gaseosa 400 ml", decimal price = 4000, decimal? cost = 2200, string tax = "Inc8",
        bool tracksStock = true, int minStock = 5, int? initialStock = 10, string? code = null, string? barcode = null,
        bool isActive = true) =>
        new { categoryId, name, code, barcode, price, cost, tax, tracksStock, minStock, isActive, initialStock };

    /// <summary>crea una categoria y un producto en ella y devuelve el id del producto</summary>
    public static async Task<Guid> CreateProductAsync(HttpClient owner, int? initialStock = 10, bool tracksStock = true, string name = "Gaseosa 400 ml")
    {
        var categoryId = await CreateCategoryAsync(owner, $"Cat {Guid.NewGuid():N}"[..20]);
        var response = await owner.PostAsJsonAsync("/api/products", NewProduct(categoryId, name, tracksStock: tracksStock, initialStock: initialStock));
        await Expect(response, HttpStatusCode.Created);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateCategoryAsync(HttpClient owner, string name = "Hamburguesas")
    {
        var response = await owner.PostAsJsonAsync("/api/categories", NewCategory(name));
        await Expect(response, HttpStatusCode.Created);
        return (await Json(response)).GetProperty("id").GetGuid();
    }
}
