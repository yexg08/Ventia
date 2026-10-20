using Ventia.Application.Auth;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using Ventia.Infrastructure.Identity;
using Ventia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ventia.Infrastructure.Auth;

public class AuthService(
    UserManager<AppUser> userManager,
    AppDbContext db,
    TokenService tokenService,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Correo o contraseña incorrectos.";
    private const string InvalidSession = "La sesión expiró. Inicia sesión de nuevo.";
    public const string AccountDisabled = "Tu cuenta está desactivada. Habla con el dueño del negocio.";
    public const string BusinessSuspended = "Este negocio está suspendido. Escríbenos si crees que es un error.";
    public const string TemporaryPasswordExpired = "Tu contraseña temporal venció. Pídele una nueva al dueño del negocio.";

    private static string? _dummyHash;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            throw new FieldValidationException(nameof(request.Email), "Ya existe una cuenta con ese correo.");

        // negocio y cuenta y rol se crean juntos o no se crea nada
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant { Name = request.BusinessName.Trim() };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim(),
            TenantId = tenant.Id,
            CreatedAt = now,
            LastLoginAt = now
        };
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            var error = created.Errors.First();
            var field = error.Code.Contains("Password") ? nameof(request.Password) : nameof(request.Email);
            throw new FieldValidationException(field, error.Description);
        }

        await userManager.AddToRoleAsync(user, Roles.Owner);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Negocio registrado {TenantId}", tenant.Id);
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // misma demora que con un correo existente (asi no se puede averiguar que correos tienen cuenta)
            var dummy = new AppUser();
            _dummyHash ??= userManager.PasswordHasher.HashPassword(dummy, Guid.NewGuid().ToString());
            userManager.PasswordHasher.VerifyHashedPassword(dummy, _dummyHash, request.Password);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Intento de inicio de sesión en cuenta bloqueada {UserId}", user.Id);
            throw new AuthenticationFailedException(
                "Cuenta bloqueada temporalmente por intentos fallidos. Intenta de nuevo en unos minutos.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            logger.LogWarning("Contraseña incorrecta para {UserId}", user.Id);
            if (await userManager.IsLockedOutAsync(user))
                logger.LogWarning("Cuenta {UserId} bloqueada por intentos fallidos", user.Id);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        // estos motivos se revelan solo DESPUES de verificar la contrasena
        await EnsureCanSignInAsync(user, ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (user.MustChangePassword && user.TemporaryPasswordExpiresAt is { } expires && expires < now)
            throw new AuthenticationFailedException(TemporaryPasswordExpired);

        user.LastLoginAt = now;
        await userManager.UpdateAsync(user);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stored = await FindTokenAsync(refreshToken, ct) ?? throw new AuthenticationFailedException(InvalidSession);

        if (stored.RevokedAt is not null)
        {
            // un token ya rotado se volvio a usar (posible robo) y se cierran todas las sesiones del usuario
            logger.LogWarning("Reuso de refresh token revocado del usuario {UserId} (se cierran sus sesiones)", stored.UserId);
            await RevokeAllAsync(db, stored.UserId, now, ct);
            throw new AuthenticationFailedException(InvalidSession);
        }

        if (!stored.IsActive(now)) throw new AuthenticationFailedException(InvalidSession);

        var user = await userManager.FindByIdAsync(stored.UserId.ToString())
            ?? throw new AuthenticationFailedException(InvalidSession);

        try
        {
            await EnsureCanSignInAsync(user, ct);
        }
        catch (AuthenticationFailedException)
        {
            await RevokeAllAsync(db, user.Id, now, ct);
            throw;
        }

        return await IssueTokensAsync(user, ct, replacing: stored);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var stored = await FindTokenAsync(refreshToken, ct);
        if (stored is null || stored.RevokedAt is not null) return;

        stored.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserDto> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("El usuario");
        return await ToDtoAsync(user, ct);
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new AuthenticationFailedException(InvalidSession);

        // el ChangePasswordAsync de Identity verifica la actual y renueva el sello de seguridad (los access tokens viejos dejan de servir)
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var mismatch = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
            throw mismatch
                ? new FieldValidationException(nameof(request.CurrentPassword), "La contraseña actual no es correcta.")
                : new FieldValidationException(nameof(request.NewPassword), result.Errors.First().Description);
        }

        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresAt = null;
        await userManager.UpdateAsync(user);
        logger.LogInformation("Contraseña cambiada por el usuario {UserId}", user.Id);

        // cierra las demas sesiones (otros dispositivos) y abre una nueva para esta
        await RevokeAllAsync(db, user.Id, timeProvider.GetUtcNow().UtcDateTime, ct);
        return await IssueTokensAsync(user, ct);
    }

    /// <summary>cuenta activa y (si pertenece a un negocio) que el negocio no este suspendido</summary>
    private async Task EnsureCanSignInAsync(AppUser user, CancellationToken ct)
    {
        if (!user.IsActive) throw new AuthenticationFailedException(AccountDisabled);
        if (user.TenantId is { } tenantId && !await db.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive, ct))
            throw new AuthenticationFailedException(BusinessSuspended);
    }

    private async Task<AuthResult> IssueTokensAsync(AppUser user, CancellationToken ct, RefreshToken? replacing = null)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var dto = await ToDtoAsync(user, ct);
        var (accessToken, accessExpiresAt) = tokenService.CreateAccessToken(
            user.Id, dto.Email, dto.FullName, [dto.Role], await userManager.GetSecurityStampAsync(user));

        var rawRefresh = TokenService.GenerateRefreshToken();
        var refreshHash = TokenService.HashRefreshToken(rawRefresh);
        var refreshExpiresAt = now.AddDays(jwtOptions.Value.RefreshTokenDays);

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedByTokenHash = refreshHash;
        }

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt
        });
        await db.SaveChangesAsync(ct);

        return new AuthResult(new AuthResponse(accessToken, accessExpiresAt, dto), rawRefresh, refreshExpiresAt);
    }

    private Task<RefreshToken?> FindTokenAsync(string rawToken, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(rawToken);
        return db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    public static async Task RevokeAllAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active) token.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserDto> ToDtoAsync(AppUser user, CancellationToken ct)
    {
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Cashier;
        var tenant = user.TenantId is { } tenantId
            ? await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new TenantSummaryDto(t.Id, t.Name))
                .FirstOrDefaultAsync(ct)
            : null;

        return new UserDto(user.Id, user.Email ?? string.Empty, user.FullName, role, tenant, user.MustChangePassword);
    }
}
