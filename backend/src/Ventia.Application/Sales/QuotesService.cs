using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Sales;

public record QuoteLineRequest(Guid ProductId, int Quantity, decimal DiscountPercent);

/// <param name="CustomerName">a nombre de quien va si no es un cliente registrado</param>
public record QuoteUpsertRequest(
    Guid? CustomerId, string? CustomerName, DateOnly ValidUntil, IReadOnlyList<QuoteLineRequest> Lines, string? Notes);

/// <summary>datos del cobro al convertir una cotizacion en venta</summary>
public record ConvertQuoteRequest(
    Guid IdempotencyKey, OrderType OrderType, string? TableLabel, string? DeliveryAddress,
    IReadOnlyList<SalePaymentRequest> Payments, decimal CashTendered);

public record QuoteLineDto(
    Guid Id, Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal DiscountPercent,
    decimal DiscountAmount, TaxType Tax, decimal LineTotal, decimal LineBase, decimal LineTax);

public record QuoteDto(
    Guid Id, int Number, DateTime CreatedAt, Guid? CustomerId, string CustomerName, DateOnly ValidUntil, bool IsExpired,
    decimal Subtotal, decimal TaxTotal, decimal DiscountTotal, decimal Total, string? Notes, QuoteStatus Status,
    Guid? SaleId, int? SaleNumber, string CreatedByName, IReadOnlyList<QuoteLineDto> Lines, ReceiptBusinessDto Business);

public record QuoteSummaryDto(
    Guid Id, int Number, DateTime CreatedAt, string CustomerName, DateOnly ValidUntil, bool IsExpired, decimal Total,
    QuoteStatus Status);

public class QuoteUpsertRequestValidator : AbstractValidator<QuoteUpsertRequest>
{
    public QuoteUpsertRequestValidator()
    {
        RuleFor(x => x.CustomerName)
            .NotEmpty().When(x => x.CustomerId is null).WithMessage("Escribe a nombre de quién va la cotización.")
            .MaximumLength(100).WithMessage("El nombre no puede superar 100 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Las notas no pueden superar 500 caracteres.");
        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("Agrega al menos un producto.")
            .Must(lines => lines is null || lines.Count <= 100).WithMessage("Máximo 100 líneas por cotización.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty().WithMessage("Falta el producto.");
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 9999).WithMessage("La cantidad debe estar entre 1 y 9.999.");
            line.RuleFor(l => l.DiscountPercent).InclusiveBetween(0, 100).WithMessage("El descuento debe estar entre 0 y 100 %.");
        });
    }
}

public class ConvertQuoteRequestValidator : AbstractValidator<ConvertQuoteRequest>
{
    public ConvertQuoteRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().WithMessage("Falta la clave del cobro.");
        RuleFor(x => x.OrderType).IsInEnum().WithMessage("El tipo de pedido no es válido.");
        RuleFor(x => x.DeliveryAddress)
            .NotEmpty().When(x => x.OrderType == OrderType.Delivery).WithMessage("Escribe la dirección del domicilio.");
        RuleFor(x => x.Payments).NotEmpty().WithMessage("Indica cómo se pagó.");
    }
}

public interface IQuotesService
{
    Task<IReadOnlyList<QuoteSummaryDto>> ListAsync(QuoteStatus? status, CancellationToken ct);
    Task<QuoteDto> GetAsync(Guid id, CancellationToken ct);
    Task<QuoteDto> CreateAsync(QuoteUpsertRequest request, CancellationToken ct);

    /// <summary>editar una cotizacion abierta toma los precios de hoy (asi se actualiza una vencida)</summary>
    Task<QuoteDto> UpdateAsync(Guid id, QuoteUpsertRequest request, CancellationToken ct);
    Task<QuoteDto> CancelAsync(Guid id, CancellationToken ct);

    /// <summary>registra la venta con los precios cotizados (necesita la caja abierta y que siga vigente)</summary>
    Task<SaleDto> ConvertAsync(Guid id, ConvertQuoteRequest request, CancellationToken ct);
}

/// <summary>cotizaciones del negocio de la sesion (las hace y las convierte cualquier miembro del negocio)</summary>
public class QuotesService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ISalesService sales,
    TimeProvider timeProvider) : IQuotesService
{
    private const int MaxValidityDays = 90;

    public async Task<IReadOnlyList<QuoteSummaryDto>> ListAsync(QuoteStatus? status, CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        var quotes = db.Quotes.AsNoTracking();
        if (status is { } s) quotes = quotes.Where(q => q.Status == s);
        return await quotes
            .OrderByDescending(q => q.Number)
            .Take(200)
            .Select(q => new QuoteSummaryDto(
                q.Id, q.Number, q.CreatedAt, q.CustomerName, q.ValidUntil,
                q.Status == QuoteStatus.Open && q.ValidUntil < today, q.Total, q.Status))
            .ToListAsync(ct);
    }

    public async Task<QuoteDto> GetAsync(Guid id, CancellationToken ct)
    {
        var quote = await db.Quotes.AsNoTracking()
            .Include(q => q.Lines.OrderBy(l => l.Position))
            .FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException("La cotización");

        var tenantId = tenant.RequireTenantId();
        var business = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new ReceiptBusinessDto(t.Name, t.TaxId, t.Address, t.City, t.Phone, t.ReceiptFooter))
            .FirstAsync(ct);
        int? saleNumber = quote.SaleId is { } saleId
            ? await db.Sales.Where(s => s.Id == saleId).Select(s => (int?)s.Number).FirstOrDefaultAsync(ct)
            : null;
        return ToDto(quote, await TodayAsync(ct), saleNumber, business);
    }

    public async Task<QuoteDto> CreateAsync(QuoteUpsertRequest request, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var quote = new Quote { CreatedByName = currentUser.Name ?? "Equipo" };
        await ApplyAsync(quote, request, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        quote.Number = await DocumentNumbers.NextAsync(db, tenantId, DocumentKind.Quote, ct);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(quote.Id, ct);
    }

    public async Task<QuoteDto> UpdateAsync(Guid id, QuoteUpsertRequest request, CancellationToken ct)
    {
        var quote = await FindOpenAsync(id, ct);
        var oldLines = await db.QuoteLines.Where(l => l.QuoteId == id).ToListAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.QuoteLines.RemoveRange(oldLines);
        quote.Lines.Clear();
        await ApplyAsync(quote, request, ct);
        // las lineas nuevas ya traen su id y se agregan explicitamente (por la navegacion EF las tomaria como existentes)
        db.QuoteLines.AddRange(quote.Lines.Select(l => { l.QuoteId = quote.Id; return l; }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<QuoteDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var quote = await FindOpenAsync(id, ct);
        quote.Status = QuoteStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SaleDto> ConvertAsync(Guid id, ConvertQuoteRequest request, CancellationToken ct)
    {
        var quote = await db.Quotes.AsNoTracking()
            .Include(q => q.Lines.OrderBy(l => l.Position))
            .FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException("La cotización");

        if (quote.Status == QuoteStatus.Converted && quote.SaleId is { } saleId)
        {
            // reintento del mismo cobro (devuelve la venta) o un segundo intento de convertirla (no se permite)
            var key = await db.Sales.Where(s => s.Id == saleId).Select(s => s.IdempotencyKey).FirstOrDefaultAsync(ct);
            if (key == request.IdempotencyKey) return await sales.GetAsync(saleId, ct);
            throw new BusinessRuleException($"La cotización {quote.Number} ya se convirtió en una venta.");
        }
        if (quote.Status == QuoteStatus.Cancelled) throw new BusinessRuleException($"La cotización {quote.Number} está cancelada.");
        if (quote.ValidUntil < await TodayAsync(ct))
            throw new BusinessRuleException(
                $"La cotización {quote.Number} venció el {quote.ValidUntil:dd/MM/yyyy}. Edítala para tomar los precios de hoy.");

        var customerId = quote.CustomerId is { } cid && await db.Customers.AnyAsync(c => c.Id == cid && !c.IsAnonymized, ct)
            ? cid
            : (Guid?)null;
        var lines = quote.Lines.OrderBy(l => l.Position).ToList();
        var saleRequest = new CreateSaleRequest(
            request.IdempotencyKey, request.OrderType, request.TableLabel, request.DeliveryAddress, customerId,
            lines.Select(l => new SaleLineRequest(l.ProductId, l.Quantity, l.DiscountPercent, null, null)).ToList(),
            request.Payments, request.CashTendered, $"Cotización {quote.Number}");

        // los precios son los cotizados y la cotizacion se marca en la misma transaccion de la venta
        // con un UPDATE condicional (si otra conversion gano la carrera no cambia ninguna fila y la venta se revierte)
        var origin = new SaleOrigin(lines.Select(l => l.UnitPrice).ToList(), async (sale, token) =>
        {
            var marked = await db.Quotes
                .Where(q => q.Id == id && q.Status == QuoteStatus.Open)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuoteStatus.Converted).SetProperty(q => q.SaleId, sale.Id), token);
            if (marked == 0) throw new BusinessRuleException($"La cotización {quote.Number} ya se convirtió en una venta.");
        });

        return await sales.CreateAsync(saleRequest, ct, origin);
    }

    private async Task ApplyAsync(Quote quote, QuoteUpsertRequest request, CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        if (request.ValidUntil < today || request.ValidUntil > today.AddDays(MaxValidityDays))
            throw new FieldValidationException(nameof(request.ValidUntil),
                $"La vigencia debe ser entre hoy y {MaxValidityDays} días.");

        if (request.CustomerId is { } customerId)
        {
            var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId && !c.IsAnonymized, ct)
                ?? throw new FieldValidationException(nameof(request.CustomerId), "El cliente no existe.");
            quote.CustomerId = customer.Id;
            quote.CustomerName = customer.Name;
        }
        else
        {
            quote.CustomerId = null;
            quote.CustomerName = request.CustomerName!.Trim();
        }

        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Count) throw new BusinessRuleException("Uno de los productos ya no existe. Actualiza la pantalla.");
        if (products.Values.FirstOrDefault(p => !p.IsActive) is { } inactive)
            throw new BusinessRuleException($"\"{inactive.Name}\" está inactivo y no se puede cotizar.");

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var product = products[line.ProductId];
            // misma regla de calculo que una venta
            var gross = product.Price * line.Quantity;
            var discount = Math.Round(gross * line.DiscountPercent / 100, 2, MidpointRounding.AwayFromZero);
            var total = gross - discount;
            var split = Taxes.Split(total, product.Tax);
            quote.Lines.Add(new QuoteLine
            {
                Position = i,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = line.Quantity,
                UnitPrice = product.Price,
                DiscountPercent = line.DiscountPercent,
                DiscountAmount = discount,
                Tax = product.Tax,
                LineTotal = total,
                LineBase = split.Base,
                LineTax = split.Tax
            });
        }

        quote.ValidUntil = request.ValidUntil;
        quote.Notes = CommonValidators.Clean(request.Notes);
        quote.Subtotal = quote.Lines.Sum(l => l.LineBase);
        quote.TaxTotal = quote.Lines.Sum(l => l.LineTax);
        quote.DiscountTotal = quote.Lines.Sum(l => l.DiscountAmount);
        quote.Total = quote.Lines.Sum(l => l.LineTotal);
    }

    private async Task<Quote> FindOpenAsync(Guid id, CancellationToken ct)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct) ?? throw new NotFoundException("La cotización");
        if (quote.Status != QuoteStatus.Open)
            throw new BusinessRuleException($"La cotización {quote.Number} ya está {(quote.Status == QuoteStatus.Converted ? "convertida en venta" : "cancelada")}.");
        return quote;
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var zone = BusinessTime.Zone(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(ct));
        return BusinessTime.Today(zone, timeProvider);
    }

    private static QuoteDto ToDto(Quote q, DateOnly today, int? saleNumber, ReceiptBusinessDto business) =>
        new(q.Id, q.Number, q.CreatedAt, q.CustomerId, q.CustomerName, q.ValidUntil,
            q.Status == QuoteStatus.Open && q.ValidUntil < today, q.Subtotal, q.TaxTotal, q.DiscountTotal, q.Total, q.Notes,
            q.Status, q.SaleId, saleNumber, q.CreatedByName,
            q.Lines.OrderBy(l => l.Position).Select(l => new QuoteLineDto(
                l.Id, l.ProductId, l.ProductName, l.Quantity, l.UnitPrice, l.DiscountPercent, l.DiscountAmount, l.Tax,
                l.LineTotal, l.LineBase, l.LineTax)).ToList(),
            business);
}
