using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Application.Sales;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Purchasing;

/// <param name="FromCashRegister">el efectivo sale de la caja abierta del dueno</param>
public record PaySupplierRequest(decimal Amount, PaymentMethod Method, DateOnly? Date, bool FromCashRegister, string? Reference);

public record VoidSupplierPaymentRequest(string Reason);

public record SupplierPaymentDto(
    Guid Id, Guid PurchaseId, DateOnly Date, decimal Amount, PaymentMethod Method, bool FromCashRegister, string? Reference,
    string CreatedByName, bool IsVoided, string? VoidedBy, string? VoidReason);

/// <summary>compra con saldo pendiente</summary>
public record PayableDto(
    Guid PurchaseId, int Number, DateOnly Date, Guid SupplierId, string SupplierName, string? SupplierInvoice,
    decimal Total, decimal PaidAmount, decimal Balance, int DaysOpen);

public class PaySupplierRequestValidator : AbstractValidator<PaySupplierRequest>
{
    public PaySupplierRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("El valor debe ser mayor que cero.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("El valor admite máximo 2 decimales.");
        RuleFor(x => x.Method).IsInEnum().WithMessage("El medio de pago no es válido.");
        RuleFor(x => x.Reference).MaximumLength(40).WithMessage("La referencia no puede superar 40 caracteres.");
        RuleFor(x => x.FromCashRegister)
            .Must((request, fromRegister) => !fromRegister || request.Method == PaymentMethod.Cash)
            .WithMessage("Solo el efectivo puede salir de la caja.");
    }
}

public class VoidSupplierPaymentRequestValidator : AbstractValidator<VoidSupplierPaymentRequest>
{
    public VoidSupplierPaymentRequestValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Escribe el motivo de la anulación.")
            .MaximumLength(200).WithMessage("El motivo no puede superar 200 caracteres.");
}

public interface ISupplierPaymentsService
{
    /// <summary>compras con saldo pendiente (las mas antiguas primero)</summary>
    Task<IReadOnlyList<PayableDto>> ListPayablesAsync(CancellationToken ct);

    /// <summary>abono o pago total (nunca mas que el saldo aunque lleguen dos pagos al mismo tiempo)</summary>
    Task<SupplierPaymentDto> PayAsync(Guid purchaseId, PaySupplierRequest request, CancellationToken ct);

    Task<SupplierPaymentDto> VoidAsync(Guid paymentId, VoidSupplierPaymentRequest request, CancellationToken ct);
}

/// <summary>pagos a proveedores del negocio de la sesion (solo el dueno)</summary>
public class SupplierPaymentsService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ICashSessionsService cashSessions,
    TimeProvider timeProvider) : ISupplierPaymentsService
{
    public async Task<IReadOnlyList<PayableDto>> ListPayablesAsync(CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        var rows = await db.Purchases.AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Completed && p.PaidAmount < p.Total)
            .OrderBy(p => p.Date).ThenBy(p => p.Number)
            .ToListAsync(ct);
        return rows.Select(p => new PayableDto(
            p.Id, p.Number, p.Date, p.SupplierId, p.SupplierName, p.SupplierInvoice, p.Total, p.PaidAmount,
            p.Total - p.PaidAmount, today.DayNumber - p.Date.DayNumber)).ToList();
    }

    public async Task<SupplierPaymentDto> PayAsync(Guid purchaseId, PaySupplierRequest request, CancellationToken ct)
    {
        var purchase = await db.Purchases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == purchaseId, ct)
            ?? throw new NotFoundException("La compra");
        if (purchase.Status == PurchaseStatus.Voided) throw new BusinessRuleException($"La compra {purchase.Number} está anulada.");

        var today = await TodayAsync(ct);
        var date = request.Date ?? today;
        if (date > today) throw new FieldValidationException(nameof(request.Date), "La fecha del pago no puede ser futura.");
        if (date < purchase.Date) throw new FieldValidationException(nameof(request.Date), "El pago no puede ser anterior a la compra.");

        Guid? sessionId = null;
        if (request.FromCashRegister)
        {
            if (date != today) throw new FieldValidationException(nameof(request.Date), "Una salida de caja es de hoy.");
            sessionId = (await cashSessions.RequireOpenAsync(ct)).Id;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // un solo UPDATE con la condicion adentro (dos abonos simultaneos nunca suman mas que el total)
        var amount = request.Amount;
        var applied = await db.Purchases
            .Where(p => p.Id == purchaseId && p.Status == PurchaseStatus.Completed && p.PaidAmount + amount <= p.Total)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAmount, p => p.PaidAmount + amount), ct);
        if (applied == 0)
        {
            var balance = await db.Purchases.Where(p => p.Id == purchaseId).Select(p => p.Total - p.PaidAmount).FirstAsync(ct);
            throw new BusinessRuleException(balance <= 0
                ? $"La compra {purchase.Number} ya está pagada."
                : $"El pago supera el saldo de la compra {purchase.Number} ({Money.Format(balance)}).");
        }

        var payment = await AddPaymentAsync(purchase, amount, request.Method, date, sessionId, request.Reference, ct);
        await transaction.CommitAsync(ct);
        return payment;
    }

    public async Task<SupplierPaymentDto> VoidAsync(Guid paymentId, VoidSupplierPaymentRequest request, CancellationToken ct)
    {
        var payment = await db.SupplierPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw new NotFoundException("El pago");
        if (payment.IsVoided) throw new BusinessRuleException("El pago ya está anulado.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // marcar la anulacion con la condicion adentro (dos anulaciones simultaneas restan una sola vez)
        var voidedAt = timeProvider.GetUtcNow().UtcDateTime;
        var voidedBy = currentUser.Name ?? "Dueño";
        var reason = request.Reason.Trim();
        var marked = await db.SupplierPayments
            .Where(p => p.Id == paymentId && !p.IsVoided)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsVoided, true)
                .SetProperty(p => p.VoidedAt, voidedAt)
                .SetProperty(p => p.VoidedBy, voidedBy)
                .SetProperty(p => p.VoidReason, reason), ct);
        if (marked == 0) throw new BusinessRuleException("El pago ya está anulado.");

        var amount = payment.Amount;
        await db.Purchases.Where(p => p.Id == payment.PurchaseId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAmount, p => p.PaidAmount - amount), ct);
        await transaction.CommitAsync(ct);

        payment.IsVoided = true;
        payment.VoidedAt = voidedAt;
        payment.VoidedBy = voidedBy;
        payment.VoidReason = reason;
        return ToDto(payment);
    }

    /// <summary>registra el pago (lo usa tambien una compra pagada de contado dentro de su propia transaccion)</summary>
    public async Task<SupplierPaymentDto> AddPaymentAsync(
        Purchase purchase, decimal amount, PaymentMethod method, DateOnly date, Guid? cashSessionId, string? reference,
        CancellationToken ct)
    {
        var payment = new SupplierPayment
        {
            PurchaseId = purchase.Id,
            SupplierId = purchase.SupplierId,
            Date = date,
            Amount = amount,
            Method = method,
            CashSessionId = cashSessionId,
            Reference = CommonValidators.Clean(reference),
            CreatedByName = currentUser.Name ?? "Dueño"
        };
        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        return ToDto(payment);
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var zone = BusinessTime.Zone(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(ct));
        return BusinessTime.Today(zone, timeProvider);
    }

    public static SupplierPaymentDto ToDto(SupplierPayment p) =>
        new(p.Id, p.PurchaseId, p.Date, p.Amount, p.Method, p.CashSessionId is not null, p.Reference, p.CreatedByName,
            p.IsVoided, p.VoidedBy, p.VoidReason);
}
