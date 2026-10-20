using Ventia.Application.Common;
using FluentValidation;

namespace Ventia.Application.Users;

/// <param name="PendingFirstLogin">aun no cambia la contrasena temporal</param>
public record UserAccountDto(
    Guid Id, string FullName, string Email, string Role, bool IsActive, bool PendingFirstLogin,
    DateTime? TemporaryPasswordExpiresAt, DateTime? LastLoginAt, DateTime CreatedAt);

public record CreateCashierRequest(string FullName, string Email);

/// <summary>la contrasena temporal va UNA sola vez en esta respuesta (en la base solo queda su hash)</summary>
public record TemporaryPasswordDto(Guid UserId, string Email, string TemporaryPassword, DateTime ExpiresAt);

public record SetUserStatusRequest(bool IsActive);

public class CreateCashierRequestValidator : AbstractValidator<CreateCashierRequest>
{
    public CreateCashierRequestValidator()
    {
        RuleFor(x => x.FullName).PersonName();
        RuleFor(x => x.Email).ValidEmail();
    }
}

/// <summary>
/// cuentas del negocio (solo el dueno las gestiona y solo puede tocar las de los cajeros)
/// el acceso se da con una contrasena temporal que vence a los 7 dias y obliga a cambiarla en el primer ingreso
/// las cuentas no se borran sino que se desactivan (las ventas guardaran quien las hizo)
/// </summary>
public interface IUsersService
{
    Task<IReadOnlyList<UserAccountDto>> ListAsync(CancellationToken ct);
    Task<TemporaryPasswordDto> CreateCashierAsync(CreateCashierRequest request, CancellationToken ct);

    /// <summary>contrasena temporal nueva (la anterior deja de servir y se cierran sus sesiones)</summary>
    Task<TemporaryPasswordDto> ResetPasswordAsync(Guid userId, CancellationToken ct);

    /// <summary>desactivar corta sus sesiones de inmediato y reactivar le devuelve el acceso</summary>
    Task<UserAccountDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct);
}
