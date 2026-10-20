using Ventia.Domain.Entities;
using FluentValidation;

namespace Ventia.Application.Sales;

// turnos de caja

public record OpenCashSessionRequest(decimal OpeningCash);

public record CloseCashSessionRequest(decimal CountedCash, string? Notes);

public record PaymentSummaryDto(PaymentMethod Method, decimal Amount);

/// <param name="ExpectedCash">base mas efectivo de las ventas no anuladas (lo que deberia haber en el cajon)</param>
public record CashSessionDto(
    Guid Id, string UserName, DateTime OpenedAt, decimal OpeningCash, DateTime? ClosedAt,
    int SalesCount, int VoidedCount, decimal SalesTotal, IReadOnlyList<PaymentSummaryDto> ByMethod,
    decimal ExpectedCash, decimal? CountedCash, decimal? Difference, string? CloseNotes);

public class OpenCashSessionRequestValidator : AbstractValidator<OpenCashSessionRequest>
{
    public OpenCashSessionRequestValidator() =>
        RuleFor(x => x.OpeningCash).InclusiveBetween(0, 100_000_000).WithMessage("La base debe estar entre 0 y 100.000.000.");
}

public class CloseCashSessionRequestValidator : AbstractValidator<CloseCashSessionRequest>
{
    public CloseCashSessionRequestValidator()
    {
        RuleFor(x => x.CountedCash).InclusiveBetween(0, 1_000_000_000).WithMessage("El efectivo contado no es válido.");
        RuleFor(x => x.Notes).MaximumLength(300).WithMessage("Las notas no pueden superar 300 caracteres.");
    }
}

// ventas

/// <param name="ParentIndex">si es una adicion (queso extra) la posicion en la lista de la linea a la que se agrega</param>
public record SaleLineRequest(Guid ProductId, int Quantity, decimal DiscountPercent, string? Note, int? ParentIndex);

public record SalePaymentRequest(PaymentMethod Method, decimal Amount);

/// <param name="IdempotencyKey">la genera el navegador por cada cobro (reintentar con la misma no crea otra venta)</param>
/// <param name="CashTendered">efectivo que entrego el cliente (para calcular el cambio)</param>
public record CreateSaleRequest(
    Guid IdempotencyKey, OrderType OrderType, string? TableLabel, string? DeliveryAddress, Guid? CustomerId,
    IReadOnlyList<SaleLineRequest> Lines, IReadOnlyList<SalePaymentRequest> Payments, decimal CashTendered, string? Notes);

public record VoidSaleRequest(string Reason);

public record SaleLineDto(
    Guid Id, int Position, Guid ProductId, string ProductName, Guid? ParentLineId, int Quantity, decimal UnitPrice,
    decimal DiscountPercent, decimal DiscountAmount, TaxType Tax, decimal LineTotal, decimal LineBase, decimal LineTax,
    string? Note);

/// <summary>datos del negocio para imprimir el comprobante</summary>
public record ReceiptBusinessDto(string Name, string? TaxId, string? Address, string? City, string? Phone, string? Footer);

public record SaleDto(
    Guid Id, int Number, DateTime CreatedAt, string SellerName, OrderType OrderType, string? TableLabel,
    string? DeliveryAddress, Guid? CustomerId, string CustomerName, string? CustomerDocument,
    decimal Subtotal, decimal TaxTotal, decimal DiscountTotal, decimal Total, decimal CashTendered, decimal Change,
    SaleStatus Status, DateTime? VoidedAt, string? VoidedBy, string? VoidReason, string? Notes,
    IReadOnlyList<SaleLineDto> Lines, IReadOnlyList<PaymentSummaryDto> Payments, ReceiptBusinessDto Business);

public record SaleSummaryDto(
    Guid Id, int Number, DateTime CreatedAt, string SellerName, OrderType OrderType, string CustomerName,
    decimal Total, SaleStatus Status, int ItemCount);

public record SalesQuery(DateOnly? From, DateOnly? To, SaleStatus? Status);

public class CreateSaleRequestValidator : AbstractValidator<CreateSaleRequest>
{
    public CreateSaleRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().WithMessage("Falta la clave del cobro.");
        RuleFor(x => x.OrderType).IsInEnum().WithMessage("El tipo de pedido no es válido.");
        RuleFor(x => x.TableLabel).MaximumLength(20).WithMessage("La mesa no puede superar 20 caracteres.");
        RuleFor(x => x.DeliveryAddress)
            .NotEmpty().When(x => x.OrderType == OrderType.Delivery).WithMessage("Escribe la dirección del domicilio.")
            .MaximumLength(150).WithMessage("La dirección no puede superar 150 caracteres.");
        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("El pedido está vacío.")
            .Must(lines => lines is null || lines.Count <= 100).WithMessage("Máximo 100 líneas por venta.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty().WithMessage("Falta el producto.");
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 999).WithMessage("La cantidad debe estar entre 1 y 999.");
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100).WithMessage("El descuento debe estar entre 0 y 100 %.");
            line.RuleFor(l => l.Note).MaximumLength(120).WithMessage("La nota no puede superar 120 caracteres.");
        });
        RuleFor(x => x.Payments).NotEmpty().WithMessage("Indica cómo se pagó.");
        RuleForEach(x => x.Payments).ChildRules(payment =>
        {
            payment.RuleFor(p => p.Method).IsInEnum().WithMessage("El medio de pago no es válido.");
            payment.RuleFor(p => p.Amount).GreaterThan(0).WithMessage("Cada pago debe ser mayor que cero.");
        });
        RuleFor(x => x.Payments)
            .Must(p => p is null || p.Count(x => x.Method == PaymentMethod.Cash) <= 1)
            .WithMessage("Registra el efectivo en un solo pago.");
        RuleFor(x => x.CashTendered).GreaterThanOrEqualTo(0).WithMessage("El efectivo recibido no es válido.");
        RuleFor(x => x.Notes).MaximumLength(300).WithMessage("Las notas no pueden superar 300 caracteres.");
    }
}

public class VoidSaleRequestValidator : AbstractValidator<VoidSaleRequest>
{
    public VoidSaleRequestValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Escribe el motivo de la anulación.")
            .MaximumLength(200).WithMessage("El motivo no puede superar 200 caracteres.");
}
