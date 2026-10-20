using System.Text.RegularExpressions;
using Ventia.Domain.Entities;

namespace Ventia.Application.Common;

/// <summary>validacion y normalizacion de documentos de identidad (misma regla en shared/documents.ts del frontend)</summary>
public static partial class Documents
{
    /// <summary>
    /// devuelve el numero normalizado o null si no es valido para ese tipo
    /// cedula de ciudadania con 3 a 10 digitos (se ignoran puntos y espacios)
    /// cedula de extranjeria y pasaporte con 3 a 15 letras o numeros (en mayusculas)
    /// NIT con digito de verificacion correcto
    /// </summary>
    public static string? Normalize(DocumentType type, string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var compact = number.Replace(".", "").Replace(" ", "").Trim();

        return type switch
        {
            DocumentType.CC => CitizenId().IsMatch(compact) ? compact : null,
            DocumentType.CE or DocumentType.PP => Alphanumeric().IsMatch(compact) ? compact.ToUpperInvariant() : null,
            DocumentType.NIT => Nit.TryNormalize(number, out var nit) ? nit : null,
            _ => null
        };
    }

    /// <summary>las empresas con NIT no necesitan autorizacion de datos personales y las personas si</summary>
    public static bool IsPerson(DocumentType type) => type != DocumentType.NIT;

    public static string InvalidMessage(DocumentType type) => type switch
    {
        DocumentType.CC => "La cédula debe tener de 3 a 10 dígitos.",
        DocumentType.NIT => "Escribe el NIT con su dígito de verificación, p. ej. 900123456-8 (revisa que el dígito sea correcto).",
        _ => "El documento debe tener de 3 a 15 letras o números."
    };

    [GeneratedRegex("^[0-9]{3,10}$")]
    private static partial Regex CitizenId();

    [GeneratedRegex("^[A-Za-z0-9]{3,15}$")]
    private static partial Regex Alphanumeric();
}
