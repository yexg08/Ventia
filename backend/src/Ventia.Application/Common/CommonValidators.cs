using System.Text.RegularExpressions;
using FluentValidation;

namespace Ventia.Application.Common;

public static partial class CommonValidators
{
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MinimumLength(10).WithMessage("La contraseña debe tener al menos 10 caracteres.")
            .MaximumLength(128).WithMessage("La contraseña no puede superar 128 caracteres.")
            .Must(p => p is not null && p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("La contraseña debe tener letras y números.");

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("El correo es obligatorio.")
            .EmailAddress().WithMessage("El correo no es válido.")
            .MaximumLength(256).WithMessage("El correo es demasiado largo.");

    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(80).WithMessage("El nombre no puede superar 80 caracteres.");

    /// <summary>NIT opcional pero si viene debe tener un digito de verificacion correcto</summary>
    public static IRuleBuilderOptions<T, string?> OptionalNit<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(n => string.IsNullOrWhiteSpace(n) || Nit.TryNormalize(n, out _))
            .WithMessage("Escribe el NIT con su dígito de verificación, p. ej. 900123456-8 (revisa que el dígito sea correcto).");

    /// <summary>telefono opcional de 7 a 15 digitos (se permiten espacios y guiones y el signo mas)</summary>
    public static IRuleBuilderOptions<T, string?> OptionalPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(p => string.IsNullOrWhiteSpace(p) || (PhoneFormat().IsMatch(p) && PhoneDigits(p)!.Length is >= 7 and <= 15))
            .WithMessage("Escribe un teléfono válido (de 7 a 15 dígitos).");

    [GeneratedRegex(@"^\+?[0-9][0-9 \-]*[0-9]$")]
    private static partial Regex PhoneFormat();

    /// <summary>solo los digitos de un telefono (null si viene vacio)</summary>
    public static string? PhoneDigits(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : new string(phone.Where(char.IsAsciiDigit).ToArray());

    /// <summary>texto opcional recortado (null si viene vacio)</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
