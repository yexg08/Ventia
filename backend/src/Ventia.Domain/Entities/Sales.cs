using Ventia.Domain.Common;

namespace Ventia.Domain.Entities;

public enum OrderType
{
    /// <summary>para comer en el local (con mesa opcional)</summary>
    DineIn,
    /// <summary>para llevar</summary>
    TakeAway,
    /// <summary>domicilio (con direccion)</summary>
    Delivery
}

public enum SaleStatus
{
    Completed,
    /// <summary>anulada por el dueno (el stock vuelve y no cuenta en caja ni en reportes)</summary>
    Voided
}

/// <summary>solo se registra como se pago (la demo no procesa pagos reales)</summary>
public enum PaymentMethod
{
    Cash,
    Card,
    Transfer
}

/// <summary>
/// turno de caja de un cajero (se abre con una base en efectivo y se cierra contando el dinero)
/// cada cuenta tiene como maximo un turno abierto y toda venta pertenece a un turno
/// </summary>
public class CashSession : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;

    public decimal OpeningCash { get; set; }

    public DateTime? ClosedAt { get; set; }

    /// <summary>efectivo que deberia haber al cerrar (base mas pagos en efectivo de ventas no anuladas)</summary>
    public decimal? ExpectedCash { get; set; }

    /// <summary>efectivo que se conto al cerrar</summary>
    public decimal? CountedCash { get; set; }

    public string? CloseNotes { get; set; }
}

/// <summary>
/// una venta (no se edita nunca y solo se anula)
/// los nombres y precios se copian al momento de vender para que el comprobante no cambie despues
/// </summary>
public class Sale : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>consecutivo del negocio sin saltos ni repetidos (lo asigna el servidor dentro de la transaccion)</summary>
    public int Number { get; set; }

    public Guid CashSessionId { get; set; }
    public Guid SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;

    public OrderType OrderType { get; set; }
    public string? TableLabel { get; set; }
    public string? DeliveryAddress { get; set; }

    /// <summary>null es consumidor final</summary>
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerDocument { get; set; }

    /// <summary>suma de las bases sin impuesto</summary>
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal DiscountTotal { get; set; }

    /// <summary>lo que se cobro (subtotal mas impuestos)</summary>
    public decimal Total { get; set; }

    /// <summary>efectivo que entrego el cliente y el cambio que se le devolvio</summary>
    public decimal CashTendered { get; set; }
    public decimal Change { get; set; }

    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }

    /// <summary>la envia el navegador por cada cobro (si llega dos veces se devuelve la misma venta)</summary>
    public Guid IdempotencyKey { get; set; }

    public string? Notes { get; set; }

    public List<SaleLine> Lines { get; set; } = [];
    public List<SalePayment> Payments { get; set; } = [];
}

/// <summary>una linea de la venta (un producto o una adicion de otro producto)</summary>
public class SaleLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public int Position { get; set; }

    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;

    /// <summary>si es una adicion (queso extra) la linea del producto al que se agrega</summary>
    public Guid? ParentLineId { get; set; }

    public int Quantity { get; set; }

    /// <summary>precio unitario con impuesto al momento de vender</summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    public TaxType Tax { get; set; }

    /// <summary>cantidad por precio menos descuento (con impuesto) y su desglose</summary>
    public decimal LineTotal { get; set; }
    public decimal LineBase { get; set; }
    public decimal LineTax { get; set; }

    /// <summary>nota para la cocina (sin cebolla)</summary>
    public string? Note { get; set; }
}

public class SalePayment : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
}
