using Ventia.Domain.Common;

namespace Ventia.Domain.Entities;

/// <summary>categoria del menu (hamburguesas o bebidas o adiciones) con la que se agrupa el punto de venta</summary>
public class Category : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>orden en el punto de venta (menor primero)</summary>
    public int SortOrder { get; set; }

    /// <summary>inactiva = no aparece en el punto de venta pero se conserva</summary>
    public bool IsActive { get; set; } = true;
}
