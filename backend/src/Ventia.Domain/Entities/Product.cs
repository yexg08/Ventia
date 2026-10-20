using Ventia.Domain.Common;

namespace Ventia.Domain.Entities;

/// <summary>producto que se vende (una hamburguesa o una gaseosa) con su precio y su impuesto</summary>
public class Product : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>codigo interno opcional (unico por negocio)</summary>
    public string? Code { get; set; }

    /// <summary>codigo de barras opcional (unico por negocio)</summary>
    public string? Barcode { get; set; }

    /// <summary>precio de venta con el impuesto incluido</summary>
    public decimal Price { get; set; }

    /// <summary>costo de compra o de preparacion (solo lo ve el dueno)</summary>
    public decimal? Cost { get; set; }

    public TaxType Tax { get; set; } = TaxType.Inc8;

    /// <summary>
    /// si es falso no se lleva inventario (una hamburguesa preparada) y si es verdadero cada venta descuenta stock
    /// (una gaseosa) y el stock nunca puede quedar negativo
    /// </summary>
    public bool TracksStock { get; set; }

    /// <summary>
    /// unidades disponibles y solo cambia junto con un movimiento de inventario (la suma de los movimientos siempre
    /// es igual a este valor) y la base de datos rechaza que sea negativo
    /// </summary>
    public int Stock { get; set; }

    /// <summary>por debajo o igual a este valor el producto sale como stock bajo</summary>
    public int MinStock { get; set; }

    /// <summary>inactivo = no aparece en el punto de venta pero se conserva</summary>
    public bool IsActive { get; set; } = true;
}

public enum StockMovementType
{
    /// <summary>stock con el que se creo el producto</summary>
    Initial,
    /// <summary>entrada manual (llego mercancia sin registrar una compra)</summary>
    In,
    /// <summary>salida manual (merma o dano o consumo interno)</summary>
    Out,
    /// <summary>ajuste por conteo fisico (la diferencia entre lo contado y lo que habia)</summary>
    Count,
    /// <summary>salida por una venta</summary>
    Sale,
    /// <summary>entrada porque se anulo una venta</summary>
    SaleVoid
}

/// <summary>
/// cada cambio del stock de un producto queda registrado aqui con quien lo hizo y el stock que quedo
/// los movimientos no se editan ni se borran
/// </summary>
public class StockMovement : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>cambio en unidades (positivo entra y negativo sale)</summary>
    public int Quantity { get; set; }

    /// <summary>stock del producto justo despues de este movimiento</summary>
    public int StockAfter { get; set; }

    public string? Reason { get; set; }

    /// <summary>venta que causo el movimiento (en ventas y anulaciones)</summary>
    public Guid? SaleId { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>nombre de quien lo hizo al momento del movimiento (se conserva aunque la cuenta cambie)</summary>
    public string UserName { get; set; } = string.Empty;
}
