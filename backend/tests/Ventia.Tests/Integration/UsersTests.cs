using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Tests.Integration;

/// <summary>cuentas de los cajeros (contrasena temporal y cambio obligatorio y desactivar y permisos del dueno)</summary>
[Collection(ApiCollection.Name)]
public class UsersTests(ApiFactory factory)
{
    private static string NewEmail() => $"cajero.{Guid.NewGuid():N}@ventia.test";

    [Fact]
    public async Task Owner_creates_a_cashier_with_a_temporary_password_that_must_be_changed()
    {
        var business = await factory.RegisterBusinessAsync();
        var email = NewEmail();

        var response = await business.Client.PostAsJsonAsync("/api/users", new { fullName = "Laura Cajera", email });
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        var created = await ApiFactory.Json(response);
        var temporary = created.GetProperty("temporaryPassword").GetString()!;
        Assert.Matches("^[A-Za-z2-9]{4}-[A-Za-z2-9]{4}-[A-Za-z2-9]{4}-[A-Za-z2-9]{4}$", temporary);

        // la lista no trae contrasenas y marca que falta el primer ingreso
        var raw = await (await business.Client.GetAsync("/api/users")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(temporary, raw);
        var list = await ApiFactory.Json(await business.Client.GetAsync("/api/users"));
        var row = Assert.Single(list.EnumerateArray(), u => u.GetProperty("email").GetString() == email);
        Assert.Equal("Cashier", row.GetProperty("role").GetString());
        Assert.True(row.GetProperty("pendingFirstLogin").GetBoolean());
        Assert.Equal("Owner", list[0].GetProperty("role").GetString());

        // con la temporal entra pero solo puede cambiarla
        var cashier = await factory.LoginAsync(email, temporary);
        await ApiFactory.Expect(await cashier.GetAsync("/api/categories"), HttpStatusCode.Forbidden);

        var changed = await cashier.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = temporary, newPassword = ApiFactory.CashierPassword });
        await ApiFactory.Expect(changed, HttpStatusCode.OK);
        ApiFactory.Authorize(cashier, await ApiFactory.Json(changed));
        await ApiFactory.Expect(await cashier.GetAsync("/api/categories"), HttpStatusCode.OK);

        // la temporal ya no sirve
        var old = await factory.NewClient().PostAsJsonAsync("/api/auth/login", new { email, password = temporary });
        await ApiFactory.Expect(old, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_temporary_password_cannot_be_used()
    {
        var business = await factory.RegisterBusinessAsync();
        var email = NewEmail();
        var created = await ApiFactory.Json(await business.Client.PostAsJsonAsync("/api/users", new { fullName = "Tarde", email }));

        await factory.WithDbAsync(null, (db, _) => db.Users.Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TemporaryPasswordExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        var login = await factory.NewClient().PostAsJsonAsync("/api/auth/login",
            new { email, password = created.GetProperty("temporaryPassword").GetString() });
        await ApiFactory.Expect(login, HttpStatusCode.Unauthorized);
        Assert.Contains("venció", (await ApiFactory.Json(login)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Resetting_the_password_closes_the_cashier_sessions()
    {
        var business = await factory.RegisterBusinessAsync();
        var cashier = await factory.CreateCashierAsync(business.Client);

        var reset = await business.Client.PostAsync($"/api/users/{cashier.UserId}/reset-password", null);
        await ApiFactory.Expect(reset, HttpStatusCode.OK);
        var temporary = (await ApiFactory.Json(reset)).GetProperty("temporaryPassword").GetString()!;

        await ApiFactory.Expect(await cashier.Client.GetAsync("/api/categories"), HttpStatusCode.Unauthorized);
        await ApiFactory.Expect(await factory.NewClient().PostAsJsonAsync("/api/auth/login",
            new { email = cashier.Email, password = ApiFactory.CashierPassword }), HttpStatusCode.Unauthorized);
        await factory.LoginAsync(cashier.Email, temporary);
    }

    [Fact]
    public async Task Deactivating_a_cashier_cuts_the_session_and_reactivating_restores_access()
    {
        var business = await factory.RegisterBusinessAsync();
        var cashier = await factory.CreateCashierAsync(business.Client);

        var off = await business.Client.PatchAsJsonAsync($"/api/users/{cashier.UserId}/status", new { isActive = false });
        await ApiFactory.Expect(off, HttpStatusCode.OK);
        Assert.False((await ApiFactory.Json(off)).GetProperty("isActive").GetBoolean());

        await ApiFactory.Expect(await cashier.Client.GetAsync("/api/categories"), HttpStatusCode.Unauthorized);
        var login = await factory.NewClient().PostAsJsonAsync("/api/auth/login",
            new { email = cashier.Email, password = ApiFactory.CashierPassword });
        await ApiFactory.Expect(login, HttpStatusCode.Unauthorized);
        Assert.Contains("desactivada", (await ApiFactory.Json(login)).GetProperty("detail").GetString());

        await ApiFactory.Expect(await business.Client.PatchAsJsonAsync($"/api/users/{cashier.UserId}/status", new { isActive = true }),
            HttpStatusCode.OK);
        await factory.LoginAsync(cashier.Email, ApiFactory.CashierPassword);
    }

    [Fact]
    public async Task The_owner_cannot_manage_owner_accounts_or_accounts_of_another_business()
    {
        var business = await factory.RegisterBusinessAsync();
        var other = await factory.RegisterBusinessAsync();
        var cashierOfOther = await factory.CreateCashierAsync(other.Client);
        var ownerId = (await ApiFactory.Json(await business.Client.GetAsync("/api/auth/me"))).GetProperty("id").GetGuid();

        // su propia cuenta de dueno no se desactiva desde aqui (409)
        await ApiFactory.Expect(await business.Client.PatchAsJsonAsync($"/api/users/{ownerId}/status", new { isActive = false }),
            HttpStatusCode.Conflict);
        await ApiFactory.Expect(await business.Client.PostAsync($"/api/users/{ownerId}/reset-password", null), HttpStatusCode.Conflict);

        // un cajero de otro negocio no existe para el (404 igual que un id inventado)
        await ApiFactory.Expect(await business.Client.PatchAsJsonAsync($"/api/users/{cashierOfOther.UserId}/status", new { isActive = false }),
            HttpStatusCode.NotFound);
        await ApiFactory.Expect(await business.Client.PostAsync($"/api/users/{cashierOfOther.UserId}/reset-password", null),
            HttpStatusCode.NotFound);
        await ApiFactory.Expect(await cashierOfOther.Client.GetAsync("/api/categories"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cashier_emails_must_be_unique_and_valid()
    {
        var business = await factory.RegisterBusinessAsync();

        var duplicated = await business.Client.PostAsJsonAsync("/api/users", new { fullName = "Copia", email = business.Email });
        await ApiFactory.Expect(duplicated, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(duplicated, "email"));

        var invalid = await business.Client.PostAsJsonAsync("/api/users", new { fullName = "Mal", email = "no-es-correo" });
        await ApiFactory.Expect(invalid, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(invalid, "email"));
    }
}
