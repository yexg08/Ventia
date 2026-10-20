namespace Ventia.Domain.Entities;

/// <summary>
/// impuestos de venta en Colombia que maneja la demo (los precios se escriben con el impuesto incluido)
/// INC es el impuesto al consumo de restaurantes y comidas rapidas
/// </summary>
public enum TaxType
{
    None,
    Iva5,
    Iva19,
    Inc8
}

/// <summary>un precio con impuesto incluido separado en base e impuesto</summary>
public readonly record struct TaxBreakdown(decimal Base, decimal Tax);

public static class Taxes
{
    /// <summary>tarifa como fraccion (0.19 para el IVA del 19 por ciento)</summary>
    public static decimal Rate(this TaxType tax) => tax switch
    {
        TaxType.Iva5 => 0.05m,
        TaxType.Iva19 => 0.19m,
        TaxType.Inc8 => 0.08m,
        _ => 0m
    };

    /// <summary>
    /// regla unica de redondeo de Ventia (la base se redondea a 2 decimales alejandose de cero y el impuesto es la
    /// diferencia) asi base mas impuesto siempre da exactamente el precio cobrado
    /// </summary>
    public static TaxBreakdown Split(decimal grossPrice, TaxType tax)
    {
        var rate = tax.Rate();
        if (rate == 0m) return new TaxBreakdown(grossPrice, 0m);
        var net = Math.Round(grossPrice / (1 + rate), 2, MidpointRounding.AwayFromZero);
        return new TaxBreakdown(net, grossPrice - net);
    }
}
