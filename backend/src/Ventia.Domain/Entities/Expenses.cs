using Ventia.Domain.Common;

namespace Ventia.Domain.Entities;

public enum ExpenseCategory
{
    Rent,
    Utilities,
    Payroll,
    Supplies,
    Maintenance,
    Marketing,
    Taxes,
    Other
}

/// <summary>
/// gasto del negocio (arriendo o servicios o nomina)
/// si se pago en efectivo desde la caja queda ligado al turno y descuenta del efectivo esperado
/// no se edita y solo se anula
/// </summary>
public class Expense : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public DateOnly Date { get; set; }
    public ExpenseCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>turno de caja del que salio el efectivo (null si se pago por fuera de la caja)</summary>
    public Guid? CashSessionId { get; set; }

    public Guid CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;

    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>
/// pago o abono a una compra (las compras a credito se van pagando hasta cubrir su total)
/// la suma de los pagos no anulados nunca supera el total de la compra
/// </summary>
public class SupplierPayment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid PurchaseId { get; set; }
    public Guid SupplierId { get; set; }

    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>turno de caja del que salio el efectivo (null si se pago por fuera de la caja)</summary>
    public Guid? CashSessionId { get; set; }

    /// <summary>numero de transferencia o recibo (opcional)</summary>
    public string? Reference { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}
