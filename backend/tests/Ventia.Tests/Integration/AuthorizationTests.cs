using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Ventia.Api.Auth;
using Ventia.Application.Common;
using Ventia.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ventia.Tests.Integration;

/// <summary>
/// "Denegar por defecto" verificado recorriendo TODOS los endpoints de la API (incluidos los que se agreguen en el
/// futuro) (cada rol solo entra a lo suyo)
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthorizationTests(ApiFactory factory)
{
    private sealed record ApiEndpoint(string Method, string Url, string Kind);

    private List<ApiEndpoint> AllEndpoints()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var result = new List<ApiEndpoint>();

        foreach (var endpoint in endpoints)
        {
            var path = endpoint.RoutePattern.RawText!;
            if (!path.StartsWith("api/")) continue;

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var kind =
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ? "anonymous"
                : authorize.Any(a => a.Policy == Policies.SuperAdmin) ? "superadmin"
                : authorize.Any(a => a.Policy == Policies.TenantOwner) ? "owner"
                : authorize.Count > 0 && authorize.All(a => a.Policy is null && a.Roles is null) ? "session"
                : "member"; // sin atributos → politica de respaldo

            // los parametros de ruta se reemplazan por un Guid (la autorizacion se evalua antes de buscar nada)
            var url = "/" + Regex.Replace(path, @"\{[^}]+\}", Guid.NewGuid().ToString());
            result.AddRange(methods.Select(method => new ApiEndpoint(method, url, kind)));
        }

        Assert.NotEmpty(result);
        return result;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, ApiEndpoint endpoint)
    {
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Url);
        if (endpoint.Method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(new { });
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Without_session_every_non_public_endpoint_returns_401()
    {
        var client = factory.NewClient();
        foreach (var endpoint in AllEndpoints().Where(e => e.Kind != "anonymous"))
        {
            var response = await SendAsync(client, endpoint);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
                $"{endpoint.Method} {endpoint.Url} devolvió {(int)response.StatusCode} sin sesión.");
        }
    }

    [Fact]
    public async Task Superadmin_cannot_use_business_endpoints()
    {
        var admin = await factory.LoginAsSuperAdminAsync();
        foreach (var endpoint in AllEndpoints().Where(e => e.Kind is "member" or "owner"))
        {
            var response = await SendAsync(admin, endpoint);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"El superadmin recibió {(int)response.StatusCode} en {endpoint.Method} {endpoint.Url}.");
        }
    }

    [Fact]
    public async Task Business_accounts_cannot_use_superadmin_endpoints()
    {
        var owner = (await factory.RegisterBusinessAsync()).Client;
        foreach (var endpoint in AllEndpoints().Where(e => e.Kind == "superadmin"))
        {
            var response = await SendAsync(owner, endpoint);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"Un dueño recibió {(int)response.StatusCode} en {endpoint.Method} {endpoint.Url}.");
        }
    }

    [Fact]
    public async Task Cashier_can_read_but_only_the_owner_manages_the_business()
    {
        var business = await factory.RegisterBusinessAsync();
        var categoryId = await ApiFactory.CreateCategoryAsync(business.Client);
        var cashier = (await factory.CreateCashierAsync(business.Client)).Client;

        await ApiFactory.Expect(await cashier.GetAsync("/api/categories"), HttpStatusCode.OK);
        await ApiFactory.Expect(await cashier.GetAsync("/api/business"), HttpStatusCode.OK);

        foreach (var endpoint in AllEndpoints().Where(e => e.Kind == "owner"))
        {
            var response = await SendAsync(cashier, endpoint);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"Un cajero recibió {(int)response.StatusCode} en {endpoint.Method} {endpoint.Url}.");
        }

        await ApiFactory.Expect(await cashier.DeleteAsync($"/api/categories/{categoryId}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_account_with_a_temporary_password_can_only_change_it()
    {
        var business = await factory.RegisterBusinessAsync();
        var created = await ApiFactory.Json(await business.Client.PostAsJsonAsync("/api/users",
            new { fullName = "Cajero nuevo", email = $"nuevo.{Guid.NewGuid():N}@ventia.test" }));
        var cashier = await factory.LoginAsync(created.GetProperty("email").GetString()!, created.GetProperty("temporaryPassword").GetString()!);

        // mientras no la cambie ninguna politica de trabajo lo deja pasar (lo aplica la API y no solo la pantalla)
        foreach (var endpoint in AllEndpoints().Where(e => e.Kind is "member" or "owner"))
        {
            var response = await SendAsync(cashier, endpoint);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"Una cuenta con contraseña temporal recibió {(int)response.StatusCode} en {endpoint.Method} {endpoint.Url}.");
        }

        var me = await ApiFactory.Json(await cashier.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
    }
}
