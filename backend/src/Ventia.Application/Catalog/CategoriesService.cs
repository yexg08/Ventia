using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Catalog;

public record CategoryDto(Guid Id, string Name, int SortOrder, bool IsActive);

public record CategoryUpsertRequest(string Name, int SortOrder, bool IsActive);

public class CategoryUpsertRequestValidator : AbstractValidator<CategoryUpsertRequest>
{
    public CategoryUpsertRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(50).WithMessage("El nombre no puede superar 50 caracteres.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 999).WithMessage("El orden debe estar entre 0 y 999.");
    }
}

public interface ICategoriesService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct);
    Task<CategoryDto> GetAsync(Guid id, CancellationToken ct);
    Task<CategoryDto> CreateAsync(CategoryUpsertRequest request, CancellationToken ct);
    Task<CategoryDto> UpdateAsync(Guid id, CategoryUpsertRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>categorias del menu (el filtro global las limita al negocio de la sesion)</summary>
public class CategoriesService(IAppDbContext db) : ICategoriesService
{
    private const string DuplicateName = "Ya tienes una categoría con ese nombre.";

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive))
            .ToListAsync(ct);

    public async Task<CategoryDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync(id, ct));

    public async Task<CategoryDto> CreateAsync(CategoryUpsertRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(name, null, ct);

        var category = new Category { Name = name, SortOrder = request.SortOrder, IsActive = request.IsActive };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, CategoryUpsertRequest request, CancellationToken ct)
    {
        var category = await FindAsync(id, ct);
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(name, id, ct);

        category.Name = name;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    // una categoria con productos no se borra (se desactiva o se mueven sus productos primero)
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var category = await FindAsync(id, ct);
        var products = await db.Products.CountAsync(p => p.CategoryId == id, ct);
        if (products > 0)
            throw new BusinessRuleException(
                $"\"{category.Name}\" tiene {products} producto(s). Muévelos a otra categoría o desactívala.");

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    // sin distinguir mayusculas (Bebidas y bebidas son la misma categoria)
    private async Task EnsureUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        if (await db.Categories.AnyAsync(c => c.Name.ToLower() == lower && c.Id != exceptId, ct))
            throw new FieldValidationException(nameof(CategoryUpsertRequest.Name), DuplicateName);
    }

    // por el filtro global una categoria de otro negocio no se encuentra (404 igual que un id inventado)
    private async Task<Category> FindAsync(Guid id, CancellationToken ct) =>
        await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("La categoría");

    private static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.SortOrder, c.IsActive);
}
