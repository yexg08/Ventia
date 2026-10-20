using System.Net;
using System.Net.Http.Json;

namespace Ventia.Tests.Integration;

[Collection(ApiCollection.Name)]
public class RegistrationTests(ApiFactory factory)
{
    private static object Register(string email, string password = ApiFactory.OwnerPassword, string businessName = "Burger Prueba") =>
        new { businessName, fullName = "Ana Gómez", email, password };

    private static string NewEmail() => $"r.{Guid.NewGuid():N}@ventia.test";

    [Fact]
    public async Task Register_creates_the_business_its_owner_and_a_session()
    {
        var client = factory.NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", Register(NewEmail()));
        await ApiFactory.Expect(response, HttpStatusCode.Created);

        var body = await ApiFactory.Json(response);
        Assert.Equal("Owner", body.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("Burger Prueba", body.GetProperty("user").GetProperty("tenant").GetProperty("name").GetString());

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("ventia_refresh="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        ApiFactory.Authorize(client, body);
        var settings = await ApiFactory.Json(await client.GetAsync("/api/business"));
        Assert.Equal("Burger Prueba", settings.GetProperty("name").GetString());
        Assert.Equal("America/Bogota", settings.GetProperty("timeZone").GetString());
        Assert.Equal("COP", settings.GetProperty("currency").GetString());
        Assert.Equal("Inc8", settings.GetProperty("defaultTax").GetString());

        // la cookie de refresh renueva la sesion
        await ApiFactory.Expect(await client.PostAsync("/api/auth/refresh", null), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Email_must_be_unique()
    {
        var existing = await factory.RegisterBusinessAsync();
        var response = await factory.NewClient().PostAsJsonAsync("/api/auth/register", Register(existing.Email));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(response, "email"));
    }

    [Theory]
    [InlineData("solo-letras-largas", "password")]
    [InlineData("corta1", "password")]
    [InlineData("1234567890", "password")]
    public async Task Weak_passwords_are_rejected(string password, string field)
    {
        var response = await factory.NewClient().PostAsJsonAsync("/api/auth/register", Register(NewEmail(), password));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(response, field));
    }

    [Fact]
    public async Task Business_name_is_required()
    {
        var response = await factory.NewClient().PostAsJsonAsync("/api/auth/register",
            Register(NewEmail(), businessName: " "));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(response, "businessName"));
    }

    [Fact]
    public async Task Login_gives_the_same_answer_for_wrong_password_and_unknown_email()
    {
        var existing = await factory.RegisterBusinessAsync();
        var client = factory.NewClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { email = existing.Email, password = "Incorrecta123" });
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new { email = "nadie@ventia.test", password = "Incorrecta123" });

        await ApiFactory.Expect(wrongPassword, HttpStatusCode.Unauthorized);
        await ApiFactory.Expect(unknownEmail, HttpStatusCode.Unauthorized);
        Assert.Equal(
            (await ApiFactory.Json(wrongPassword)).GetProperty("detail").GetString(),
            (await ApiFactory.Json(unknownEmail)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account()
    {
        var existing = await factory.RegisterBusinessAsync();
        var client = factory.NewClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new { email = existing.Email, password = "Incorrecta123" });

        var locked = await client.PostAsJsonAsync("/api/auth/login", new { email = existing.Email, password = ApiFactory.OwnerPassword });
        await ApiFactory.Expect(locked, HttpStatusCode.Unauthorized);
        Assert.Contains("bloqueada", (await ApiFactory.Json(locked)).GetProperty("detail").GetString());
    }
}
