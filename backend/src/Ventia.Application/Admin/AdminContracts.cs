namespace Ventia.Application.Admin;

public record AdminTenantDto(
    Guid Id, string Name, string? TaxId, string OwnerName, string OwnerEmail, DateTime CreatedAt, bool IsActive,
    int UserCount, DateTime? LastLoginAt);

public record AdminStatsDto(int Businesses, int ActiveBusinesses, int NewLast30Days, int Users);

public record SetTenantStatusRequest(bool IsActive);

/// <summary>panel del superadmin (ve todos los negocios a proposito y por eso ignora el filtro de negocio)</summary>
public interface IAdminService
{
    Task<IReadOnlyList<AdminTenantDto>> ListTenantsAsync(string? search, CancellationToken ct);
    Task<AdminStatsDto> GetStatsAsync(CancellationToken ct);
    Task SetTenantStatusAsync(Guid tenantId, bool isActive, CancellationToken ct);
}
