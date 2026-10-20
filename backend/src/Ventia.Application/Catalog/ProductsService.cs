using System.Text.RegularExpressions;
using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Application.Inventory;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Catalog;

/// <param name="Cost">null para el cajero (los costos y margenes solo los ve el dueno y lo decide el servidor)</param>
/// <param name="MarginPercent">margen sobre el precio sin impuesto (null si no hay costo o para el cajero)</param>
public record ProductDto(
    Guid Id, Guid CategoryId, string CategoryName, string Name, string? Code, string? Barcode,
    decimal Price, decimal BasePrice, decimal TaxAmount, TaxType Tax, decimal? Cost, decimal? MarginPercent,
    bool TracksStock, int Stock, int MinStock, bool IsLowStock, bool IsActive);

/// <param name="InitialStock">solo al crear y si el producto lleva inventario</param>
public record ProductUpsertRequest(
    Guid CategoryId, string Name, string? Code, string? Barcode, decimal Price, decimal? Cost, TaxType Tax,
    bool TracksStock, int MinStock, bool IsActive, int? InitialStock);

public enum StockAdjustmentKind
{
    /// <summary>entran unidades</summary>
    In,
    /// <summary>salen unidades</summary>
    Out,
    /// <summary>el stock queda en lo que se conto</summary>
    Count
}

public record StockAdjustmentRequest(StockAdjustmentKind Kind, int Quantity, string Reason);

public record StockMovementDto(
    Guid Id, DateTime CreatedAt, StockMovementType Type, int Quantity, int StockAfter, string? Reason, string UserName);

public record ProductQuery(string? Search, Guid? CategoryId, bool LowStock);

public partial class ProductUpsertRequestValidator : AbstractValidator<ProductUpsertRequest>
{
    public ProductUpsertRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("Elige una categoría.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(80).WithMessage("El nombre no puede superar 80 caracteres.");
        RuleFor(x => x.Code)
            .Must(c => string.IsNullOrWhiteSpace(c) || CodeFormat().IsMatch(c.Trim()))
            .WithMessage("Usa hasta 30 letras, números o guiones.");
        RuleFor(x => x.Barcode)
            .Must(c => string.IsNullOrWhiteSpace(c) || CodeFormat().IsMatch(c.Trim()))
            .WithMessage("Usa hasta 30 letras, números o guiones.");
        RuleFor(x => x.Price)
            .InclusiveBetween(0, 100_000_000).WithMessage("El precio debe estar entre 0 y 100.000.000.")
            .Must(HasAtMostTwoDecimals).WithMessage("El precio admite máximo 2 decimales.");
        RuleFor(x => x.Cost)
            .Must(c => c is null || (c >= 0 && c <= 100_000_000 && HasAtMostTwoDecimals(c.Value)))
            .WithMessage("El costo debe estar entre 0 y 100.000.000 (máximo 2 decimales).");
        RuleFor(x => x.Tax).IsInEnum().WithMessage("El impuesto no es válido.");
        RuleFor(x => x.MinStock).InclusiveBetween(0, 1_000_000).WithMessage("El stock mínimo debe estar entre 0 y 1.000.000.");
        RuleFor(x => x.InitialStock)
            .InclusiveBetween(0, 1_000_000).When(x => x.InitialStock is not null)
            .WithMessage("El stock inicial debe estar entre 0 y 1.000.000.");
    }

    private static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, 2) == value;

    [GeneratedRegex("^[A-Za-z0-9-]{1,30}$")]
    private static partial Regex CodeFormat();
}

public class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("El tipo de ajuste no es válido.");
        RuleFor(x => x.Quantity)
            .InclusiveBetween(1, 1_000_000).When(x => x.Kind != StockAdjustmentKind.Count)
            .WithMessage("La cantidad debe estar entre 1 y 1.000.000.");
        RuleFor(x => x.Quantity)
            .InclusiveBetween(0, 1_000_000).When(x => x.Kind == StockAdjustmentKind.Count)
            .WithMessage("El conteo debe estar entre 0 y 1.000.000.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Escribe el motivo del ajuste.")
            .MaximumLength(120).WithMessage("El motivo no puede superar 120 caracteres.");
    }
}

public interface IProductsService
{
    Task<IReadOnlyList<ProductDto>> ListAsync(ProductQuery query, CancellationToken ct);
    Task<ProductDto> GetAsync(Guid id, CancellationToken ct);
    Task<ProductDto> CreateAsync(ProductUpsertRequest request, CancellationToken ct);
    Task<ProductDto> UpdateAsync(Guid id, ProductUpsertRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<ProductDto> AdjustStockAsync(Guid id, StockAdjustmentRequest request, CancellationToken ct);
    Task<IReadOnlyList<StockMovementDto>> ListMovementsAsync(Guid id, CancellationToken ct);
}

/// <summary>productos del negocio de la sesion (el filtro global limita todo a ese negocio)</summary>
public class ProductsService(IAppDbContext db, IStockService stock, ICurrentUser currentUser) : IProductsService
{
    private const string DuplicateCode = "Ya tienes un producto con ese código.";
    private const string DuplicateBarcode = "Ya tienes un producto con ese código de barras.";

    public async Task<IReadOnlyList<ProductDto>> ListAsync(ProductQuery query, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) || (p.Code != null && p.Code.ToLower() == term) || p.Barcode == term);
        }
        if (query.CategoryId is { } categoryId) products = products.Where(p => p.CategoryId == categoryId);
        if (query.LowStock) products = products.Where(p => p.TracksStock && p.Stock <= p.MinStock);

        var rows = await products
            .Join(db.Categories, p => p.CategoryId, c => c.Id, (p, c) => new { Product = p, Category = c })
            .OrderBy(x => x.Category.SortOrder).ThenBy(x => x.Category.Name).ThenBy(x => x.Product.Name)
            .ToListAsync(ct);
        return rows.Select(r => ToDto(r.Product, r.Category.Name)).ToList();
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct)
    {
        var product = await FindAsync(id, tracked: false, ct);
        return ToDto(product, await CategoryNameAsync(product.CategoryId, ct));
    }

    public async Task<ProductDto> CreateAsync(ProductUpsertRequest request, CancellationToken ct)
    {
        var categoryName = await CategoryNameAsync(request.CategoryId, ct);
        var product = new Product();
        Apply(product, request);
        await EnsureUniqueCodesAsync(product, ct);

        var initial = request.TracksStock ? request.InitialStock ?? 0 : 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        if (initial > 0) await stock.ChangeAsync(product.Id, initial, StockMovementType.Initial, "Stock inicial", ct);
        await transaction.CommitAsync(ct);

        return ToDto(await FindAsync(product.Id, tracked: false, ct), categoryName);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, ProductUpsertRequest request, CancellationToken ct)
    {
        var product = await FindAsync(id, tracked: true, ct);
        var categoryName = await CategoryNameAsync(request.CategoryId, ct);

        // el stock no se toca desde aqui (solo con ajustes o ventas) y dejar de llevar inventario exige stock en cero
        if (product.TracksStock && !request.TracksStock && product.Stock != 0)
            throw new BusinessRuleException(
                $"\"{product.Name}\" todavía tiene {product.Stock} unidades. Ajusta el stock a 0 antes de dejar de llevar su inventario.");

        Apply(product, request);
        await EnsureUniqueCodesAsync(product, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(product, categoryName);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await FindAsync(id, tracked: true, ct);

        // con movimientos reales (ajustes y despues ventas) se pierde el historial y por eso solo se desactiva
        if (await db.StockMovements.AnyAsync(m => m.ProductId == id && m.Type != StockMovementType.Initial, ct)
            || await db.SaleLines.AnyAsync(l => l.ProductId == id, ct))
            throw new BusinessRuleException(
                $"\"{product.Name}\" ya tiene ventas o movimientos de inventario. Desactívalo para que no salga en el punto de venta.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.StockMovements.RemoveRange(await db.StockMovements.Where(m => m.ProductId == id).ToListAsync(ct));
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<ProductDto> AdjustStockAsync(Guid id, StockAdjustmentRequest request, CancellationToken ct)
    {
        _ = request.Kind switch
        {
            StockAdjustmentKind.In => await stock.ChangeAsync(id, request.Quantity, StockMovementType.In, request.Reason, ct),
            StockAdjustmentKind.Out => await stock.ChangeAsync(id, -request.Quantity, StockMovementType.Out, request.Reason, ct),
            _ => await stock.SetCountedAsync(id, request.Quantity, request.Reason, ct)
        };
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<StockMovementDto>> ListMovementsAsync(Guid id, CancellationToken ct)
    {
        await FindAsync(id, tracked: false, ct);
        return await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(200)
            .Select(m => new StockMovementDto(m.Id, m.CreatedAt, m.Type, m.Quantity, m.StockAfter, m.Reason, m.UserName))
            .ToListAsync(ct);
    }

    private static void Apply(Product product, ProductUpsertRequest request)
    {
        product.CategoryId = request.CategoryId;
        product.Name = request.Name.Trim();
        product.Code = CommonValidators.Clean(request.Code)?.ToUpperInvariant();
        product.Barcode = CommonValidators.Clean(request.Barcode);
        product.Price = request.Price;
        product.Cost = request.Cost;
        product.Tax = request.Tax;
        product.TracksStock = request.TracksStock;
        product.MinStock = request.TracksStock ? request.MinStock : 0;
        product.IsActive = request.IsActive;
    }

    private async Task EnsureUniqueCodesAsync(Product product, CancellationToken ct)
    {
        if (product.Code is { } code && await db.Products.AnyAsync(p => p.Code == code && p.Id != product.Id, ct))
            throw new FieldValidationException(nameof(Product.Code), DuplicateCode);
        if (product.Barcode is { } barcode && await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != product.Id, ct))
            throw new FieldValidationException(nameof(Product.Barcode), DuplicateBarcode);
    }

    // la categoria debe ser del mismo negocio (una ajena no existe para el filtro global)
    private async Task<string> CategoryNameAsync(Guid categoryId, CancellationToken ct) =>
        await db.Categories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(ct)
        ?? throw new FieldValidationException(nameof(ProductUpsertRequest.CategoryId), "La categoría no existe.");

    private async Task<Product> FindAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.Products : db.Products.AsNoTracking();
        return await query.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("El producto");
    }

    private ProductDto ToDto(Product p, string categoryName)
    {
        var split = Taxes.Split(p.Price, p.Tax);
        var owner = currentUser.IsOwner;
        decimal? margin = owner && p.Cost is { } cost && split.Base > 0
            ? Math.Round((split.Base - cost) / split.Base * 100, 1, MidpointRounding.AwayFromZero)
            : null;
        return new ProductDto(
            p.Id, p.CategoryId, categoryName, p.Name, p.Code, p.Barcode, p.Price, split.Base, split.Tax, p.Tax,
            owner ? p.Cost : null, margin, p.TracksStock, p.Stock, p.MinStock,
            p.TracksStock && p.Stock <= p.MinStock, p.IsActive);
    }
}
