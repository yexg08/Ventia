using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Application.Inventory;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Purchasing;

public record PurchaseLineRequest(Guid ProductId, int Quantity, decimal UnitCost);

/// <param name="Date">dia de la compra (si no viene es hoy en la hora del negocio)</param>
public record CreatePurchaseRequest(
    Guid SupplierId, string? SupplierInvoice, DateOnly? Date, IReadOnlyList<PurchaseLineRequest> Lines, string? Notes);

public record VoidPurchaseRequest(string Reason);

public record PurchaseLineDto(Guid Id, Guid ProductId, string ProductName, int Quantity, decimal UnitCost, decimal LineTotal);

public record PurchaseDto(
    Guid Id, int Number, DateOnly Date, Guid SupplierId, string SupplierName, string? SupplierInvoice, decimal Total,
    string? Notes, PurchaseStatus Status, DateTime? VoidedAt, string? VoidedBy, string? VoidReason, string CreatedByName,
    DateTime CreatedAt, IReadOnlyList<PurchaseLineDto> Lines);

public record PurchaseSummaryDto(
    Guid Id, int Number, DateOnly Date, string SupplierName, string? SupplierInvoice, decimal Total, PurchaseStatus Status,
    int ItemCount);

public record PurchasesQuery(DateOnly? From, DateOnly? To, PurchaseStatus? Status);

public class CreatePurchaseRequestValidator : AbstractValidator<CreatePurchaseRequest>
{
    public CreatePurchaseRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("Elige el proveedor.");
        RuleFor(x => x.SupplierInvoice).MaximumLength(30).WithMessage("El número de factura no puede superar 30 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(300).WithMessage("Las notas no pueden superar 300 caracteres.");
        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("Agrega al menos un producto.")
            .Must(lines => lines is null || lines.Count <= 100).WithMessage("Máximo 100 líneas por compra.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty().WithMessage("Falta el producto.");
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 1_000_000).WithMessage("La cantidad debe estar entre 1 y 1.000.000.");
            line.RuleFor(l => l.UnitCost)
                .InclusiveBetween(0, 100_000_000).WithMessage("El costo debe estar entre 0 y 100.000.000.")
                .Must(c => decimal.Round(c, 2) == c).WithMessage("El costo admite máximo 2 decimales.");
        });
    }
}

public class VoidPurchaseRequestValidator : AbstractValidator<VoidPurchaseRequest>
{
    public VoidPurchaseRequestValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Escribe el motivo de la anulación.")
            .MaximumLength(200).WithMessage("El motivo no puede superar 200 caracteres.");
}

public interface IPurchasesService
{
    Task<IReadOnlyList<PurchaseSummaryDto>> ListAsync(PurchasesQuery query, CancellationToken ct);
    Task<PurchaseDto> GetAsync(Guid id, CancellationToken ct);

    /// <summary>registra la compra y en la misma transaccion sube el stock y recalcula el costo promedio</summary>
    Task<PurchaseDto> CreateAsync(CreatePurchaseRequest request, CancellationToken ct);

    /// <summary>anula la compra y saca del inventario lo que habia entrado (si ya se vendio no se puede)</summary>
    Task<PurchaseDto> VoidAsync(Guid id, VoidPurchaseRequest request, CancellationToken ct);
}

/// <summary>compras del negocio de la sesion (solo las gestiona el dueno)</summary>
public class PurchasesService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IStockService stock,
    TimeProvider timeProvider) : IPurchasesService
{
    public async Task<IReadOnlyList<PurchaseSummaryDto>> ListAsync(PurchasesQuery query, CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-30);
        if (from > to) (from, to) = (to, from);

        var purchases = db.Purchases.AsNoTracking().Where(p => p.Date >= from && p.Date <= to);
        if (query.Status is { } status) purchases = purchases.Where(p => p.Status == status);

        return await purchases
            .OrderByDescending(p => p.Number)
            .Take(500)
            .Select(p => new PurchaseSummaryDto(
                p.Id, p.Number, p.Date, p.SupplierName, p.SupplierInvoice, p.Total, p.Status, p.Lines.Sum(l => l.Quantity)))
            .ToListAsync(ct);
    }

    public async Task<PurchaseDto> GetAsync(Guid id, CancellationToken ct)
    {
        var purchase = await db.Purchases.AsNoTracking()
            .Include(p => p.Lines.OrderBy(l => l.Position))
            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("La compra");
        return ToDto(purchase);
    }

    public async Task<PurchaseDto> CreateAsync(CreatePurchaseRequest request, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct)
            ?? throw new FieldValidationException(nameof(request.SupplierId), "El proveedor no existe.");
        if (!supplier.IsActive) throw new BusinessRuleException($"\"{supplier.Name}\" está inactivo. Actívalo para registrarle compras.");

        var today = await TodayAsync(ct);
        var date = request.Date ?? today;
        if (date > today) throw new FieldValidationException(nameof(request.Date), "La fecha de la compra no puede ser futura.");

        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Count) throw new BusinessRuleException("Uno de los productos ya no existe. Actualiza la pantalla.");
        // las compras alimentan el inventario (un producto sin inventario no tiene stock que subir)
        if (products.Values.FirstOrDefault(p => !p.TracksStock) is { } untracked)
            throw new BusinessRuleException($"\"{untracked.Name}\" no lleva inventario. Actívaselo para registrar sus compras.");

        var purchase = new Purchase
        {
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            SupplierInvoice = CommonValidators.Clean(request.SupplierInvoice),
            Date = date,
            Notes = CommonValidators.Clean(request.Notes),
            CreatedByName = currentUser.Name ?? "Dueño"
        };
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            purchase.Lines.Add(new PurchaseLine
            {
                Position = i,
                ProductId = line.ProductId,
                ProductName = products[line.ProductId].Name,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                LineTotal = line.Quantity * line.UnitCost
            });
        }
        purchase.Total = purchase.Lines.Sum(l => l.LineTotal);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        purchase.Number = await DocumentNumbers.NextAsync(db, tenantId, DocumentKind.Purchase, ct);
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync(ct);

        foreach (var group in purchase.Lines.GroupBy(l => l.ProductId))
        {
            var quantity = group.Sum(l => l.Quantity);
            var unitCost = Math.Round(group.Sum(l => l.LineTotal) / quantity, 2, MidpointRounding.AwayFromZero);
            await UpdateAverageCostAsync(group.Key, quantity, unitCost, ct);
            await stock.ChangeAsync(group.Key, quantity, StockMovementType.Purchase, $"Compra {purchase.Number}", ct, purchaseId: purchase.Id);
        }

        await transaction.CommitAsync(ct);
        return await GetAsync(purchase.Id, ct);
    }

    public async Task<PurchaseDto> VoidAsync(Guid id, VoidPurchaseRequest request, CancellationToken ct)
    {
        var purchase = await db.Purchases.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("La compra");
        if (purchase.Status == PurchaseStatus.Voided) throw new BusinessRuleException($"La compra {purchase.Number} ya está anulada.");

        var entered = await db.StockMovements.AsNoTracking()
            .Where(m => m.PurchaseId == purchase.Id && m.Type == StockMovementType.Purchase)
            .Select(m => new { m.ProductId, m.Quantity })
            .ToListAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var movement in entered)
                await stock.ChangeAsync(movement.ProductId, -movement.Quantity, StockMovementType.PurchaseVoid,
                    $"Anulación compra {purchase.Number}", ct, purchaseId: purchase.Id);
        }
        catch (BusinessRuleException e)
        {
            // si parte de lo comprado ya se vendio no hay como sacarlo del inventario
            throw new BusinessRuleException($"No se puede anular la compra {purchase.Number}. {e.Message}");
        }

        purchase.Status = PurchaseStatus.Voided;
        purchase.VoidedAt = timeProvider.GetUtcNow().UtcDateTime;
        purchase.VoidedBy = currentUser.Name ?? "Dueño";
        purchase.VoidReason = request.Reason.Trim();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// costo promedio ponderado calculado por la base en un solo UPDATE (lo que habia por su costo mas lo que entra
    /// por el nuevo costo dividido entre el total) y si no habia stock o costo el nuevo costo es el de la compra
    /// la anulacion de una compra devuelve el stock pero no recalcula el costo
    /// </summary>
    private Task UpdateAverageCostAsync(Guid productId, int quantity, decimal unitCost, CancellationToken ct) =>
        db.Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                p => p.Cost,
                p => p.Stock <= 0 || p.Cost == null
                    ? unitCost
                    : Math.Round(((decimal)p.Stock * p.Cost.Value + quantity * unitCost) / (p.Stock + quantity), 2)), ct);

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var zone = BusinessTime.Zone(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(ct));
        return BusinessTime.Today(zone, timeProvider);
    }

    private static PurchaseDto ToDto(Purchase p) =>
        new(p.Id, p.Number, p.Date, p.SupplierId, p.SupplierName, p.SupplierInvoice, p.Total, p.Notes, p.Status, p.VoidedAt,
            p.VoidedBy, p.VoidReason, p.CreatedByName, p.CreatedAt,
            p.Lines.OrderBy(l => l.Position)
                .Select(l => new PurchaseLineDto(l.Id, l.ProductId, l.ProductName, l.Quantity, l.UnitCost, l.LineTotal)).ToList());
}
