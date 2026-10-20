using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Infrastructure.Auth;

namespace Ventia.Api.Services;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(AppClaims.UserId)?.Value, out var id) ? id : null;

    public string? Name => accessor.HttpContext?.User.FindFirst(AppClaims.Name)?.Value;

    public bool IsOwner => accessor.HttpContext?.User.IsInRole(Roles.Owner) ?? false;
}

/// <summary>negocio de la peticion (el claim tenant_id que SessionValidator agrega desde la base de datos)</summary>
public class TenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private bool _overridden;
    private Guid? _override;

    public Guid? TenantId =>
        _overridden ? _override
        : Guid.TryParse(accessor.HttpContext?.User.FindFirst(AppClaims.TenantId)?.Value, out var id) ? id : null;

    public void Set(Guid? tenantId)
    {
        _overridden = true;
        _override = tenantId;
    }
}
