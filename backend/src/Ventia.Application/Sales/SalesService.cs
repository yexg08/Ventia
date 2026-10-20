using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Application.Inventory;
using Ventia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Sales;

/// <summary>
/// venta que nace de otro documento (una cotizacion) con los precios de ese documento por linea
/// y lo que hay que marcar en el mismo momento en que la venta queda registrada
/// </summary>
public record SaleOrigin(IReadOnlyList<decimal> UnitPrices, Func<Sale, CancellationToken, Task> OnCreatedAsync);

public interface ISalesService
{
    /// <summary>registra una venta completa (todo o nada) o devuelve la misma si la clave del cobro ya se uso</summary>
    Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken ct, SaleOrigin? origin = null);
    Task<SaleDto> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<SaleSummaryDto>> ListAsync(SalesQuery query, CancellationToken ct);

    /// <summary>anula la venta y devuelve al inventario lo que se habia descontado</summary>
    Task<SaleDto> VoidAsync(Guid id, VoidSaleRequest request, CancellationToken ct);
}

/// <summary>
/// ventas del negocio de la sesion
/// el navegador solo dice que productos y cuantos (precios e impuestos y totales los calcula el servidor)
/// el cajero solo ve sus propias ventas y el dueno las de todo el negocio
/// </summary>
public class SalesService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ICashSessionsService cashSessions,
    IStockService stock,
    TimeProvider timeProvider) : ISalesService
{
    public const string FinalConsumer = "Consumidor final";

    public async Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken ct, SaleOrigin? origin = null)
    {
        var tenantId = tenant.RequireTenantId();

        // la misma clave otra vez (doble clic o reintento por mala conexion) devuelve la venta ya hecha
        if (await IdByKeyAsync(request.IdempotencyKey, ct) is { } existingId) return await GetAsync(existingId, ct);

        var session = await cashSessions.RequireOpenAsync(ct);
        var (sale, products) = await BuildAsync(request, session, origin, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // el consecutivo se toma dentro de la transaccion (ver DocumentNumbers)
            sale.Number = await DocumentNumbers.NextAsync(db, tenantId, DocumentKind.Sale, ct);

            db.Sales.Add(sale);
            await db.SaveChangesAsync(ct);

            // descuenta el stock en la misma transaccion (si algo no alcanza no queda ni la venta ni el descuento)
            foreach (var group in sale.Lines.Where(l => products[l.ProductId].TracksStock).GroupBy(l => l.ProductId))
                await stock.ChangeAsync(group.Key, -group.Sum(l => l.Quantity), StockMovementType.Sale,
                    $"Venta {sale.Number}", ct, sale.Id);

            // por ejemplo marcar la cotizacion como convertida en la misma transaccion
            if (origin is not null) await origin.OnCreatedAsync(sale, ct);

            await transaction.CommitAsync(ct);
        }
        catch (DuplicateRequestException)
        {
            // otra peticion con la misma clave gano la carrera y se devuelve esa venta
            await transaction.RollbackAsync(ct);
            return await GetAsync(await IdByKeyAsync(request.IdempotencyKey, ct) ?? throw new InvalidOperationException(), ct);
        }

        return await GetAsync(sale.Id, ct);
    }

    public async Task<SaleDto> GetAsync(Guid id, CancellationToken ct)
    {
        var sale = await VisibleSales().AsNoTracking()
            .Include(s => s.Lines.OrderBy(l => l.Position))
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("La venta");

        var tenantId = tenant.RequireTenantId();
        var business = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new ReceiptBusinessDto(t.Name, t.TaxId, t.Address, t.City, t.Phone, t.ReceiptFooter))
            .FirstAsync(ct);
        return ToDto(sale, business);
    }

    public async Task<IReadOnlyList<SaleSummaryDto>> ListAsync(SalesQuery query, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var zone = BusinessTime.Zone(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(ct));
        var to = query.To ?? BusinessTime.Today(zone, timeProvider);
        var from = query.From ?? to;
        if (from > to) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 92) throw new BusinessRuleException("Consulta máximo 3 meses a la vez.");

        var start = BusinessTime.StartOfDayUtc(from, zone);
        var end = BusinessTime.StartOfDayUtc(to.AddDays(1), zone);
        var sales = VisibleSales().AsNoTracking().Where(s => s.CreatedAt >= start && s.CreatedAt < end);
        if (query.Status is { } status) sales = sales.Where(s => s.Status == status);

        return await sales
            .OrderByDescending(s => s.Number)
            .Take(500)
            .Select(s => new SaleSummaryDto(
                s.Id, s.Number, s.CreatedAt, s.SellerName, s.OrderType, s.CustomerName, s.Total, s.Status,
                s.Lines.Where(l => l.ParentLineId == null).Sum(l => l.Quantity)))
            .ToListAsync(ct);
    }

    public async Task<SaleDto> VoidAsync(Guid id, VoidSaleRequest request, CancellationToken ct)
    {
        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("La venta");
        if (sale.Status == SaleStatus.Voided) throw new BusinessRuleException($"La venta {sale.Number} ya está anulada.");

        // se devuelve exactamente lo que la venta desconto (segun sus movimientos y no segun el producto de hoy)
        var taken = await db.StockMovements.AsNoTracking()
            .Where(m => m.SaleId == sale.Id && m.Type == StockMovementType.Sale)
            .Select(m => new { m.ProductId, m.Quantity })
            .ToListAsync(ct);
        var stillTracked = await db.Products.Where(p => p.TracksStock).Select(p => p.Id).ToListAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var movement in taken.Where(m => stillTracked.Contains(m.ProductId)))
            await stock.ChangeAsync(movement.ProductId, -movement.Quantity, StockMovementType.SaleVoid,
                $"Anulación venta {sale.Number}", ct, sale.Id);

        sale.Status = SaleStatus.Voided;
        sale.VoidedAt = timeProvider.GetUtcNow().UtcDateTime;
        sale.VoidedBy = currentUser.Name ?? "Dueño";
        sale.VoidReason = request.Reason.Trim();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await GetAsync(id, ct);
    }

    private async Task<(Sale Sale, Dictionary<Guid, Product> Products)> BuildAsync(
        CreateSaleRequest request, CashSession session, SaleOrigin? origin, CancellationToken ct)
    {
        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        // por el filtro global un producto de otro negocio no aparece y cuenta como inexistente
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Count) throw new BusinessRuleException("Uno de los productos ya no existe. Actualiza la pantalla.");
        if (products.Values.FirstOrDefault(p => !p.IsActive) is { } inactive)
            throw new BusinessRuleException($"\"{inactive.Name}\" está inactivo y no se puede vender.");

        var sale = new Sale
        {
            CashSessionId = session.Id,
            SellerId = currentUser.UserId ?? throw new UnauthorizedAccessException(),
            SellerName = currentUser.Name ?? "Cajero",
            OrderType = request.OrderType,
            TableLabel = request.OrderType == OrderType.DineIn ? CommonValidators.Clean(request.TableLabel) : null,
            DeliveryAddress = request.OrderType == OrderType.Delivery ? CommonValidators.Clean(request.DeliveryAddress) : null,
            IdempotencyKey = request.IdempotencyKey,
            Notes = CommonValidators.Clean(request.Notes),
            CustomerName = FinalConsumer
        };

        if (request.CustomerId is { } customerId)
        {
            var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId && !c.IsAnonymized, ct)
                ?? throw new FieldValidationException(nameof(request.CustomerId), "El cliente no existe.");
            sale.CustomerId = customer.Id;
            sale.CustomerName = customer.Name;
            sale.CustomerDocument = $"{customer.DocumentType} {customer.DocumentNumber}";
        }

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var product = products[line.ProductId];
            var unitPrice = origin?.UnitPrices[i] ?? product.Price;
            var gross = unitPrice * line.Quantity;
            var discount = Math.Round(gross * line.DiscountPercent / 100, 2, MidpointRounding.AwayFromZero);
            var total = gross - discount;
            var split = Taxes.Split(total, product.Tax);
            sale.Lines.Add(new SaleLine
            {
                Position = i,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = line.Quantity,
                UnitPrice = unitPrice,
                DiscountPercent = line.DiscountPercent,
                DiscountAmount = discount,
                Tax = product.Tax,
                LineTotal = total,
                LineBase = split.Base,
                LineTax = split.Tax,
                Note = CommonValidators.Clean(line.Note)
            });
        }

        // adiciones (una adicion va sobre un producto principal y no sobre otra adicion)
        for (var i = 0; i < request.Lines.Count; i++)
        {
            if (request.Lines[i].ParentIndex is not { } parent) continue;
            if (parent < 0 || parent >= request.Lines.Count || parent == i || request.Lines[parent].ParentIndex is not null)
                throw new BusinessRuleException("Una adición debe ir sobre un producto del pedido.");
            sale.Lines[i].ParentLineId = sale.Lines[parent].Id;
        }

        sale.Subtotal = sale.Lines.Sum(l => l.LineBase);
        sale.TaxTotal = sale.Lines.Sum(l => l.LineTax);
        sale.DiscountTotal = sale.Lines.Sum(l => l.DiscountAmount);
        sale.Total = sale.Lines.Sum(l => l.LineTotal);

        var paid = request.Payments.Sum(p => p.Amount);
        if (paid != sale.Total)
            throw new BusinessRuleException($"Los pagos suman {Money.Format(paid)} y el total es {Money.Format(sale.Total)}.");
        sale.Payments.AddRange(request.Payments.Select(p => new SalePayment { Method = p.Method, Amount = p.Amount }));

        var cash = request.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        if (cash > 0)
        {
            var tendered = request.CashTendered == 0 ? cash : request.CashTendered;
            if (tendered < cash) throw new BusinessRuleException("El efectivo recibido es menor que lo que se paga en efectivo.");
            sale.CashTendered = tendered;
            sale.Change = tendered - cash;
        }

        return (sale, products);
    }

    private IQueryable<Sale> VisibleSales() =>
        currentUser.IsOwner ? db.Sales : db.Sales.Where(s => s.SellerId == currentUser.UserId);

    private async Task<Guid?> IdByKeyAsync(Guid key, CancellationToken ct) =>
        await db.Sales.AsNoTracking().Where(s => s.IdempotencyKey == key).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);

    private static SaleDto ToDto(Sale s, ReceiptBusinessDto business) =>
        new(s.Id, s.Number, s.CreatedAt, s.SellerName, s.OrderType, s.TableLabel, s.DeliveryAddress, s.CustomerId,
            s.CustomerName, s.CustomerDocument, s.Subtotal, s.TaxTotal, s.DiscountTotal, s.Total, s.CashTendered, s.Change,
            s.Status, s.VoidedAt, s.VoidedBy, s.VoidReason, s.Notes,
            s.Lines.OrderBy(l => l.Position).Select(l => new SaleLineDto(
                l.Id, l.Position, l.ProductId, l.ProductName, l.ParentLineId, l.Quantity, l.UnitPrice, l.DiscountPercent,
                l.DiscountAmount, l.Tax, l.LineTotal, l.LineBase, l.LineTax, l.Note)).ToList(),
            s.Payments.OrderBy(p => p.Method).Select(p => new PaymentSummaryDto(p.Method, p.Amount)).ToList(),
            business);
}
