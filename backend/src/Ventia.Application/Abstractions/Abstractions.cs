using Ventia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }

    // todos los siguientes se filtran solos al negocio de la sesion
    DbSet<Category> Categories { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>usuario autenticado de la peticion actual (null fuera de una peticion HTTP)</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}

/// <summary>
/// negocio de la peticion actual
/// en el panel sale de la base de datos al validar la sesion (nunca del token ni de la peticion)
/// es null para el superadmin y las peticiones anonimas (y entonces el filtro global no deja ver ningun dato de negocio)
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    /// <summary>fija el negocio a mano (solo para procesos sin peticion HTTP como el seed o las pruebas)</summary>
    void Set(Guid? tenantId);
}

public static class TenantContextExtensions
{
    public static Guid RequireTenantId(this ITenantContext tenant) =>
        tenant.TenantId ?? throw new UnauthorizedAccessException("La sesión no pertenece a ningún negocio.");
}
