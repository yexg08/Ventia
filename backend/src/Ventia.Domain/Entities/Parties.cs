using Ventia.Domain.Common;

namespace Ventia.Domain.Entities;

/// <summary>tipos de documento de identidad en Colombia que maneja la demo</summary>
public enum DocumentType
{
    /// <summary>cedula de ciudadania</summary>
    CC,
    /// <summary>cedula de extranjeria</summary>
    CE,
    /// <summary>NIT de empresa o persona con su digito de verificacion</summary>
    NIT,
    /// <summary>pasaporte</summary>
    PP
}

/// <summary>cliente del negocio (las ventas de la fase 4 pueden ir a nombre de un cliente o a consumidor final)</summary>
public class Customer : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public DocumentType DocumentType { get; set; }

    /// <summary>normalizado (solo digitos para la cedula y 900123456-8 para el NIT)</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    /// <summary>notas internas del negocio que el cliente no ve</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// cuando el cliente autorizo el tratamiento de sus datos (Ley 1581 de 2012)
    /// es obligatorio para personas y opcional para empresas con NIT
    /// </summary>
    public DateTime? DataConsentAt { get; set; }

    /// <summary>
    /// se pidio borrar sus datos pero tiene ventas (se borraron sus datos personales y se conserva el registro
    /// para que las ventas no queden huerfanas) y ya no aparece en las busquedas
    /// </summary>
    public bool IsAnonymized { get; set; }
}

/// <summary>proveedor del negocio (a quien se le compra la mercancia en la fase 5)</summary>
public class Supplier : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public DocumentType DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>persona con quien se habla (el vendedor o el asesor)</summary>
    public string? ContactName { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    /// <summary>inactivo = no aparece al registrar compras pero se conserva</summary>
    public bool IsActive { get; set; } = true;
}
