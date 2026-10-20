using Microsoft.AspNetCore.Identity;

namespace Ventia.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    public string FullName { get; set; } = string.Empty;

    /// <summary>negocio al que pertenece la cuenta - null solo para el superadmin</summary>
    public Guid? TenantId { get; set; }

    /// <summary>desactivar corta el acceso de inmediato (se revisa en cada peticion)</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// la contrasena actual es temporal (la genero el dueno del negocio) y hasta cambiarla la cuenta solo puede usar
    /// el endpoint de cambio de contrasena
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>hasta cuando sirve la contrasena temporal (7 dias desde que se genero)</summary>
    public DateTime? TemporaryPasswordExpiresAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole() => Id = Guid.CreateVersion7();

    public AppRole(string name) : this() => Name = name;
}

/// <summary>mensajes de Identity en espanol (los que pueden llegar a verse en pantalla)</summary>
public class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "La contraseña actual no es correcta." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"La contraseña debe tener al menos {length} caracteres." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "La contraseña debe tener al menos un número." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "La contraseña debe tener al menos una letra minúscula." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Ya existe una cuenta con ese correo." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Ya existe una cuenta con ese correo." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "El correo no es válido." };
}
