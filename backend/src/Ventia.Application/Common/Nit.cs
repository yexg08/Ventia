namespace Ventia.Application.Common;

/// <summary>NIT colombiano con su digito de verificacion (algoritmo de la DIAN con pesos primos y modulo 11)</summary>
public static class Nit
{
    private static readonly int[] Weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>digito de verificacion de un numero de NIT (solo digitos y sin el digito)</summary>
    public static int CheckDigit(string number)
    {
        var sum = 0;
        for (var i = 0; i < number.Length; i++)
            sum += (number[number.Length - 1 - i] - '0') * Weights[i];
        var remainder = sum % 11;
        return remainder > 1 ? 11 - remainder : remainder;
    }

    /// <summary>
    /// acepta 900123456-8 o 900.123.456-8 (el digito va despues del guion) y devuelve 900123456-8
    /// falso si el formato no sirve o el digito de verificacion no corresponde
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var parts = input.Replace(".", "").Replace(" ", "").Split('-');
        if (parts.Length != 2) return false;

        var (number, digit) = (parts[0], parts[1]);
        if (number.Length is < 6 or > 15 || !number.All(char.IsAsciiDigit)) return false;
        if (digit.Length != 1 || !char.IsAsciiDigit(digit[0])) return false;
        if (CheckDigit(number) != digit[0] - '0') return false;

        normalized = $"{number}-{digit}";
        return true;
    }
}
