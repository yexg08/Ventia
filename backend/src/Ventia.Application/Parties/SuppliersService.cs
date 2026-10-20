using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Parties;

public record SupplierDto(
    Guid Id, DocumentType DocumentType, string DocumentNumber, string Name, string? ContactName, string? Phone,
    string? Email, string? Address, string? Notes, bool IsActive);

public record SupplierUpsertRequest(
    DocumentType DocumentType, string DocumentNumber, string Name, string? ContactName, string? Phone, string? Email,
    string? Address, string? Notes, bool IsActive);

public class SupplierUpsertRequestValidator : AbstractValidator<SupplierUpsertRequest>
{
    public SupplierUpsertRequestValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum().WithMessage("El tipo de documento no es válido.");
        RuleFor(x => x.DocumentNumber)
            .Must((request, number) => Documents.Normalize(request.DocumentType, number) is not null)
            .WithMessage(request => Documents.InvalidMessage(request.DocumentType));
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar 100 caracteres.");
        RuleFor(x => x.ContactName).MaximumLength(80).WithMessage("El contacto no puede superar 80 caracteres.");
        RuleFor(x => x.Phone).OptionalPhone();
        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("El correo no es válido.")
            .MaximumLength(256).WithMessage("El correo es demasiado largo.");
        RuleFor(x => x.Address).MaximumLength(120).WithMessage("La dirección no puede superar 120 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Las notas no pueden superar 500 caracteres.");
    }
}

public interface ISuppliersService
{
    Task<IReadOnlyList<SupplierDto>> ListAsync(string? search, CancellationToken ct);
    Task<SupplierDto> GetAsync(Guid id, CancellationToken ct);
    Task<SupplierDto> CreateAsync(SupplierUpsertRequest request, CancellationToken ct);
    Task<SupplierDto> UpdateAsync(Guid id, SupplierUpsertRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>proveedores del negocio de la sesion (solo los gestiona el dueno)</summary>
public class SuppliersService(IAppDbContext db) : ISuppliersService
{
    public async Task<IReadOnlyList<SupplierDto>> ListAsync(string? search, CancellationToken ct)
    {
        var suppliers = db.Suppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            suppliers = suppliers.Where(s =>
                s.Name.ToLower().Contains(term)
                || s.DocumentNumber.ToLower().StartsWith(term)
                || (s.ContactName != null && s.ContactName.ToLower().Contains(term)));
        }

        return await suppliers.OrderBy(s => s.Name).Select(s => ToDto(s)).ToListAsync(ct);
    }

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync(id, ct));

    public async Task<SupplierDto> CreateAsync(SupplierUpsertRequest request, CancellationToken ct)
    {
        var supplier = new Supplier();
        await ApplyAsync(supplier, request, ct);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return ToDto(supplier);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, SupplierUpsertRequest request, CancellationToken ct)
    {
        var supplier = await FindAsync(id, ct);
        await ApplyAsync(supplier, request, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(supplier);
    }

    // un proveedor con compras no se borra (se desactiva para no perder el historial)
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var supplier = await FindAsync(id, ct);
        if (await db.Purchases.AnyAsync(p => p.SupplierId == id, ct))
            throw new BusinessRuleException($"\"{supplier.Name}\" tiene compras registradas. Desactívalo en vez de eliminarlo.");
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Supplier supplier, SupplierUpsertRequest request, CancellationToken ct)
    {
        var number = Documents.Normalize(request.DocumentType, request.DocumentNumber)!;
        if (await db.Suppliers.AnyAsync(s =>
                s.DocumentType == request.DocumentType && s.DocumentNumber == number && s.Id != supplier.Id, ct))
            throw new FieldValidationException(nameof(request.DocumentNumber), "Ya tienes un proveedor con ese documento.");

        supplier.DocumentType = request.DocumentType;
        supplier.DocumentNumber = number;
        supplier.Name = request.Name.Trim();
        supplier.ContactName = CommonValidators.Clean(request.ContactName);
        supplier.Phone = CommonValidators.PhoneDigits(request.Phone);
        supplier.Email = CommonValidators.Clean(request.Email)?.ToLowerInvariant();
        supplier.Address = CommonValidators.Clean(request.Address);
        supplier.Notes = CommonValidators.Clean(request.Notes);
        supplier.IsActive = request.IsActive;
    }

    private async Task<Supplier> FindAsync(Guid id, CancellationToken ct) =>
        await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("El proveedor");

    private static SupplierDto ToDto(Supplier s) =>
        new(s.Id, s.DocumentType, s.DocumentNumber, s.Name, s.ContactName, s.Phone, s.Email, s.Address, s.Notes, s.IsActive);
}
