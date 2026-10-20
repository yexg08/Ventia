using System.Security.Claims;
using Ventia.Infrastructure.Auth;
using Ventia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Api.Auth;

/// <summary>
/// se ejecuta en CADA peticion autenticada despues de validar la firma del JWT - consulta la base de datos para
/// que los cambios sean inmediatos (sin esperar los 15 minutos que dura el token)
/// - cuenta desactivada o negocio suspendido (rechazada)
/// - sello de seguridad distinto (contrasena cambiada) (rechazada)
/// - agrega el TenantId leido de la base de datos - ese claim es la unica fuente del negocio de la sesion
/// </summary>
public static class SessionValidator
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity is not ClaimsIdentity identity
            || !Guid.TryParse(principal.FindFirstValue(AppClaims.UserId), out var userId))
        {
            context.Fail("Token sin usuario.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var session = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.IsActive,
                u.SecurityStamp,
                u.TenantId,
                u.MustChangePassword,
                TenantActive = u.TenantId == null || db.Tenants.Any(t => t.Id == u.TenantId && t.IsActive)
            })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (session is null || !session.IsActive || !session.TenantActive)
        {
            context.Fail("Cuenta inactiva o negocio suspendido.");
            return;
        }

        if (principal.FindFirstValue(AppClaims.SecurityStamp) != session.SecurityStamp)
        {
            context.Fail("Sesión revocada.");
            return;
        }

        // por si alguien fabricara un token con estos claims (se descartan y se reconstruyen desde la base)
        foreach (var claim in identity.FindAll(c => c.Type is AppClaims.TenantId or AppClaims.PasswordChangeRequired).ToList())
            identity.RemoveClaim(claim);
        if (session.TenantId is { } tenantId) identity.AddClaim(new Claim(AppClaims.TenantId, tenantId.ToString()));
        if (session.MustChangePassword) identity.AddClaim(new Claim(AppClaims.PasswordChangeRequired, "true"));
    }
}
