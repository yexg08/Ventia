using System.Security.Cryptography;
using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Application.Users;
using Ventia.Infrastructure.Auth;
using Ventia.Infrastructure.Identity;
using Ventia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ventia.Infrastructure.Users;

public class UsersService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<UsersService> logger) : IUsersService
{
    public static readonly TimeSpan TemporaryPasswordLifetime = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<UserAccountDto>> ListAsync(CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new
            {
                User = u,
                Role = db.UserRoles.Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        // primero el dueno y luego los cajeros por nombre
        return rows
            .OrderBy(r => r.Role == Roles.Owner ? 0 : 1).ThenBy(r => r.User.FullName)
            .Select(r => ToDto(r.User, r.Role ?? Roles.Cashier))
            .ToList();
    }

    public async Task<TemporaryPasswordDto> CreateCashierAsync(CreateCashierRequest request, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            throw new FieldValidationException(nameof(request.Email), "Ya existe una cuenta con ese correo.");

        var password = GeneratePassword();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim(),
            TenantId = tenantId,
            MustChangePassword = true,
            TemporaryPasswordExpiresAt = now.Add(TemporaryPasswordLifetime),
            CreatedAt = now
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded) throw new FieldValidationException(nameof(request.Email), created.Errors.First().Description);
        await userManager.AddToRoleAsync(user, Roles.Cashier);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Cuenta de cajero {UserId} creada en el negocio {TenantId}", user.Id, tenantId);
        return new TemporaryPasswordDto(user.Id, email, password, user.TemporaryPasswordExpiresAt.Value);
    }

    public async Task<TemporaryPasswordDto> ResetPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await FindCashierAsync(userId, ct);
        var password = GeneratePassword();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // reemplaza la contrasena y renueva el sello de seguridad (los access tokens viejos dejan de servir al instante)
        await userManager.RemovePasswordAsync(user);
        var added = await userManager.AddPasswordAsync(user, password);
        if (!added.Succeeded) throw new BusinessRuleException(added.Errors.First().Description);
        await userManager.UpdateSecurityStampAsync(user);

        user.MustChangePassword = true;
        user.TemporaryPasswordExpiresAt = now.Add(TemporaryPasswordLifetime);
        await userManager.UpdateAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await AuthService.RevokeAllAsync(db, user.Id, now, ct);

        logger.LogWarning("Contraseña temporal nueva para la cuenta {UserId}", user.Id);
        return new TemporaryPasswordDto(user.Id, user.Email!, password, user.TemporaryPasswordExpiresAt.Value);
    }

    public async Task<UserAccountDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct)
    {
        var user = await FindCashierAsync(userId, ct);
        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            await userManager.UpdateAsync(user);
            // el SessionValidator ya rechaza sus access tokens y ademas se revocan los refresh tokens
            if (!isActive) await AuthService.RevokeAllAsync(db, user.Id, timeProvider.GetUtcNow().UtcDateTime, ct);
            logger.LogWarning("Cuenta {UserId} {Action} por el dueño del negocio", user.Id, isActive ? "reactivada" : "desactivada");
        }

        return ToDto(user, Roles.Cashier);
    }

    /// <summary>
    /// solo cuentas de cajero del negocio de la sesion (una cuenta de otro negocio da 404 igual que un id inventado)
    /// el dueno no puede tocar su propia cuenta desde aqui ni la de otro dueno
    /// </summary>
    private async Task<AppUser> FindCashierAsync(Guid userId, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct)
            ?? throw new NotFoundException("La cuenta");
        if (user.Id == currentUser.UserId || !await userManager.IsInRoleAsync(user, Roles.Cashier))
            throw new BusinessRuleException("Solo puedes administrar las cuentas de los cajeros.");
        return user;
    }

    private static UserAccountDto ToDto(AppUser u, string role) =>
        new(u.Id, u.FullName, u.Email!, role, u.IsActive, u.MustChangePassword, u.TemporaryPasswordExpiresAt, u.LastLoginAt, u.CreatedAt);

    /// <summary>16 caracteres en 4 grupos sin caracteres que se confundan (0 y O o 1 y l e I) y con al menos un numero</summary>
    public static string GeneratePassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        while (true)
        {
            var chars = Enumerable.Range(0, 16).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
            if (!chars.Any(char.IsDigit) || !chars.Any(char.IsLetter)) continue;
            return string.Join('-', Enumerable.Range(0, 4).Select(i => new string(chars, i * 4, 4)));
        }
    }
}
