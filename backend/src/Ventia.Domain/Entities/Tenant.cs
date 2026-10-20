using Ventia.Domain.Common;

namespace Ventia.Domain.Entities;

/// <summary>un negocio registrado en Ventia (sus datos quedan aislados de los demas negocios)</summary>
public class Tenant : Entity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>NIT con digito de verificacion (por ejemplo 900123456-8) y opcional en la demo</summary>
    public string? TaxId { get; set; }

    public string? Address { get; set; }
    public string? City { get; set; }

    /// <summary>solo digitos (con indicativo si se escribio)</summary>
    public string? Phone { get; set; }

    /// <summary>zona horaria IANA en la que trabaja el negocio</summary>
    public string TimeZone { get; set; } = "America/Bogota";

    public string Currency { get; set; } = "COP";

    /// <summary>impuesto con el que se crean los productos nuevos (comida rapida suele usar el INC del 8 por ciento)</summary>
    public TaxType DefaultTax { get; set; } = TaxType.Inc8;

    /// <summary>texto al pie del comprobante (por ejemplo gracias por tu compra)</summary>
    public string? ReceiptFooter { get; set; }

    /// <summary>
    /// un negocio suspendido por el superadmin no puede iniciar sesion y sus sesiones abiertas se cortan de inmediato
    /// no se borra nada (se puede reactivar)
    /// </summary>
    public bool IsActive { get; set; } = true;
}
