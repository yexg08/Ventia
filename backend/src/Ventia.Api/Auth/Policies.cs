using Ventia.Application.Common;
using Ventia.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Ventia.Api.Auth;

/// <summary>
/// politicas de autorizacion
///
/// denegar por defecto - <see cref="TenantMember"/> es la politica de respaldo (FallbackPolicy) - todo endpoint que no
/// declare otra cosa exige una cuenta de un negocio (dueno o cajero) con su negocio cargado desde la base de datos
/// asi un endpoint nuevo nunca queda abierto al publico ni al superadmin por olvido - excepciones explicitas
/// - [AllowAnonymous] (registro y login y refresh y logout y /api/health)
/// - [Authorize] solo sesion (/auth/me y /auth/change-password)
/// - [Authorize(Policy = TenantOwner)] (acciones que solo hace el dueno del negocio)
/// - [Authorize(Policy = SuperAdmin)] (el panel de la plataforma)
/// </summary>
public static class Policies
{
    public const string TenantMember = "TenantMember";
    public const string TenantOwner = "TenantOwner";
    public const string SuperAdmin = "SuperAdmin";

    public static void Configure(AuthorizationOptions options)
    {
        var member = WithTenant().RequireRole(Roles.TenantMembers).Build();

        options.AddPolicy(TenantMember, member);
        options.AddPolicy(TenantOwner, WithTenant().RequireRole(Roles.Owner).Build());
        options.AddPolicy(SuperAdmin, Working().RequireRole(Roles.SuperAdmin).Build());

        options.FallbackPolicy = member;
        options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }

    /// <summary>el claim tenant_id lo agrega SessionValidator desde la base de datos no viene en el token</summary>
    private static AuthorizationPolicyBuilder WithTenant() => Working().RequireClaim(AppClaims.TenantId);

    /// <summary>
    /// sesion que ya puede trabajar - una cuenta con contrasena temporal (claim pwd_change) no pasa ninguna politica de)
    /// trabajo - solo puede usar /auth/me y /auth/change-password que piden solo sesion
    /// </summary>
    private static AuthorizationPolicyBuilder Working() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.HasClaim(c => c.Type == AppClaims.PasswordChangeRequired));
}
