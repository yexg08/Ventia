using System.Globalization;
using System.Text;

namespace Ventia.Application.Common;

/// <summary>
/// CSV que Excel en espanol abre bien con doble clic (separador punto y coma con coma decimal y UTF-8 con BOM para las tildes)
/// </summary>
public sealed class Csv
{
    private const char Separator = ';';
    private readonly StringBuilder builder = new();

    public Csv(params string[] headers) => Row(headers.Select(Text).ToArray());

    /// <summary>agrega una fila con celdas ya convertidas con Text Number o Date</summary>
    public Csv Row(params string[] cells)
    {
        builder.AppendJoin(Separator, cells).Append("\r\n");
        return this;
    }

    public byte[] ToBytes() => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();

    /// <summary>
    /// texto escrito por usuarios (un nombre o una descripcion)
    /// si empieza como una formula se antepone un apostrofo para que Excel no la ejecute (inyeccion de CSV)
    /// </summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([Separator, '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string Number(decimal? value) => value is { } v ? Number(v) : string.Empty;

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Date(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
