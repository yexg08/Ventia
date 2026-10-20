using System.Globalization;

namespace Ventia.Application.Common;

public static class Money
{
    /// <summary>
    /// 18000 da 18.000 (miles con punto como en Colombia) sin depender del idioma del servidor
    /// en Docker no hay datos de cultura y es-CO no se puede usar
    /// </summary>
    public static string Format(decimal amount)
    {
        var text = amount.ToString(amount == decimal.Truncate(amount) ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);
        return text.Replace(',', '_').Replace('.', ',').Replace('_', '.');
    }
}
