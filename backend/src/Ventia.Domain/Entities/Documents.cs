using Ventia.Domain.Common;

namespace Ventia.Domain.Entities;

public enum PurchaseStatus
{
    Completed,
    /// <summary>anulada (se quito del inventario lo que habia entrado)</summary>
    Voided
}

/// <summary>
/// compra de mercancia a un proveedor (sube el stock y recalcula el costo promedio de cada producto)
/// no se edita y solo se anula
/// </summary>
public class Purchase : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>consecutivo propio de las compras (sin saltos ni repetidos)</summary>
    public int Number { get; set; }

    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;

    /// <summary>numero de la factura del proveedor (opcional)</summary>
    public string? SupplierInvoice { get; set; }

    /// <summary>dia de la compra en la hora local del negocio</summary>
    public DateOnly Date { get; set; }

    public decimal Total { get; set; }

    /// <summary>suma de los pagos no anulados (nunca mayor que el total) y el saldo es la diferencia</summary>
    public decimal PaidAmount { get; set; }

    public string? Notes { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Completed;
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public List<PurchaseLine> Lines { get; set; } = [];
}

public class PurchaseLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid PurchaseId { get; set; }
    public int Position { get; set; }

    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    /// <summary>costo por unidad que cobro el proveedor</summary>
    public decimal UnitCost { get; set; }

    public decimal LineTotal { get; set; }
}

public enum QuoteStatus
{
    Open,
    /// <summary>se convirtio en una venta</summary>
    Converted,
    Cancelled
}

/// <summary>
/// cotizacion para un cliente (un evento o un pedido grande) con los precios del dia en que se hizo
/// mientras este vigente se puede convertir en venta respetando esos precios
/// </summary>
public class Quote : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }

    public Guid? CustomerId { get; set; }

    /// <summary>a nombre de quien va (el cliente registrado o un nombre libre)</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>ultimo dia en que se respetan los precios (hora local del negocio)</summary>
    public DateOnly ValidUntil { get; set; }

    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }

    public QuoteStatus Status { get; set; } = QuoteStatus.Open;

    /// <summary>venta en que se convirtio</summary>
    public Guid? SaleId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public List<QuoteLine> Lines { get; set; } = [];
}

public class QuoteLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid QuoteId { get; set; }
    public int Position { get; set; }

    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>precio con impuesto al momento de cotizar</summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public TaxType Tax { get; set; }
    public decimal LineTotal { get; set; }
    public decimal LineBase { get; set; }
    public decimal LineTax { get; set; }
}
