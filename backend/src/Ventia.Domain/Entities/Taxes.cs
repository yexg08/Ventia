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
}
