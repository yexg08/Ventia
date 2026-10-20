using Ventia.Application.Admin;
using Ventia.Application.Common;
using Ventia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ventia.Infrastructure.Admin;

public class AdminService(AppDbContext db, TimeProvider timeProvider, ILogger<AdminService> logger) : IAdminService
{
    public async Task<IReadOnlyList<AdminTenantDto>> ListTenantsAsync(string? search, CancellationToken ct)
    {
        var ownerRoleId = await db.Roles.Where(r => r.Name == Roles.Owner).Select(r => r.Id).FirstOrDefaultAsync(ct);

        var query = db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, pattern, "\\")
                || (t.TaxId != null && EF.Functions.ILike(t.TaxId, pattern, "\\"))
                || db.Users.Any(u => u.TenantId == t.Id && EF.Functions.ILike(u.Email!, pattern, "\\")));
        }

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id, t.Name, t.TaxId, t.CreatedAt, t.IsActive,
                Owner = db.Users
                    .Where(u => u.TenantId == t.Id && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == ownerRoleId))
                    .OrderBy(u => u.CreatedAt)
                    .Select(u => new { u.FullName, u.Email })
                    .FirstOrDefault(),
                UserCount = db.Users.Count(u => u.TenantId == t.Id),
                LastLoginAt = db.Users.Where(u => u.TenantId == t.Id).Max(u => u.LastLoginAt)
            })
            .ToListAsync(ct);

        return rows.Select(r => new AdminTenantDto(
            r.Id, r.Name, r.TaxId, r.Owner?.FullName ?? "—", r.Owner?.Email ?? "—", r.CreatedAt, r.IsActive,
            r.UserCount, r.LastLoginAt)).ToList();
    }

    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken ct)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);
        return new AdminStatsDto(
            await db.Tenants.CountAsync(ct),
            await db.Tenants.CountAsync(t => t.IsActive, ct),
            await db.Tenants.CountAsync(t => t.CreatedAt >= since, ct),
            await db.Users.CountAsync(u => u.TenantId != null, ct));
    }

    public async Task SetTenantStatusAsync(Guid tenantId, bool isActive, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException("El negocio");
        if (tenant.IsActive == isActive) return;

        tenant.IsActive = isActive;
        await db.SaveChangesAsync(ct);

        if (!isActive)
        {
            // los access tokens ya mueren en la siguiente peticion (SessionValidator revisa el negocio)
            // y ademas se revocan los refresh tokens para que no se puedan renovar
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await db.RefreshTokens
                .Where(rt => rt.RevokedAt == null && db.Users.Any(u => u.Id == rt.UserId && u.TenantId == tenantId))
                .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, now), ct);
        }

        logger.LogWarning("Negocio {TenantId} {Action} por el superadmin", tenant.Id, isActive ? "reactivado" : "suspendido");
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
