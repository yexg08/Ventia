using Ventia.Application.Abstractions;
using Ventia.Domain.Common;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Parties;

public record CustomerDto(
    Guid Id, DocumentType DocumentType, string DocumentNumber, string Name, string? Phone, string? Email,
    string? Address, string? Notes, DateTime? DataConsentAt, DateTime CreatedAt);

/// <param name="DataConsent">el cliente autorizo el tratamiento de sus datos (obligatorio si es persona)</param>
public record CustomerUpsertRequest(
    DocumentType DocumentType, string DocumentNumber, string Name, string? Phone, string? Email, string? Address,
    string? Notes, bool DataConsent);

public class CustomerUpsertRequestValidator : AbstractValidator<CustomerUpsertRequest>
{
    public CustomerUpsertRequestValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum().WithMessage("El tipo de documento no es válido.");
        RuleFor(x => x.DocumentNumber)
            .Must((request, number) => Documents.Normalize(request.DocumentType, number) is not null)
            .WithMessage(request => Documents.InvalidMessage(request.DocumentType));
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar 100 caracteres.");
        RuleFor(x => x.Phone).OptionalPhone();
        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("El correo no es válido.")
            .MaximumLength(256).WithMessage("El correo es demasiado largo.");
        RuleFor(x => x.Address).MaximumLength(120).WithMessage("La dirección no puede superar 120 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Las notas no pueden superar 500 caracteres.");
    }
}

public interface ICustomersService
{
    Task<IReadOnlyList<CustomerDto>> ListAsync(string? search, CancellationToken ct);
    Task<CustomerDto> GetAsync(Guid id, CancellationToken ct);
    Task<CustomerDto> CreateAsync(CustomerUpsertRequest request, CancellationToken ct);
    Task<CustomerDto> UpdateAsync(Guid id, CustomerUpsertRequest request, CancellationToken ct);

    /// <summary>borra al cliente y sus datos personales (derecho de supresion de la Ley 1581)</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>clientes del negocio de la sesion (el filtro global limita todo a ese negocio)</summary>
public class CustomersService(IAppDbContext db, TimeProvider timeProvider) : ICustomersService
{
    private const int MaxResults = 200;

    public async Task<IReadOnlyList<CustomerDto>> ListAsync(string? search, CancellationToken ct)
    {
        var customers = db.Customers.AsNoTracking().Where(c => !c.IsAnonymized);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
            customers = customers.Where(c =>
                c.Name.ToLower().Contains(term)
                || c.DocumentNumber.ToLower().StartsWith(term)
                || (digits.Length >= 3 && c.Phone != null && c.Phone.Contains(digits)));
        }

        return await customers
            .OrderBy(c => c.Name)
            .Take(MaxResults)
            .Select(c => ToDto(c))
            .ToListAsync(ct);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync(id, ct));

    public async Task<CustomerDto> CreateAsync(CustomerUpsertRequest request, CancellationToken ct)
    {
        var customer = new Customer();
        await ApplyAsync(customer, request, ct);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, CustomerUpsertRequest request, CancellationToken ct)
    {
        var customer = await FindAsync(id, ct);
        await ApplyAsync(customer, request, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    // un cliente con ventas se anonimiza (se borran sus datos personales pero las ventas se conservan)
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var customer = await FindAsync(id, ct);
        if (!await db.Sales.AnyAsync(s => s.CustomerId == id, ct))
        {
            db.Customers.Remove(customer);
            await db.SaveChangesAsync(ct);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        customer.Name = AnonymizedName;
        customer.DocumentNumber = $"X{id:N}"[..20];
        customer.Phone = null;
        customer.Email = null;
        customer.Address = null;
        customer.Notes = null;
        customer.DataConsentAt = null;
        customer.IsAnonymized = true;
        await db.SaveChangesAsync(ct);
        // los comprobantes guardaban una copia del nombre y el documento
        await db.Sales.Where(s => s.CustomerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerName, AnonymizedName).SetProperty(x => x.CustomerDocument, (string?)null), ct);
        await transaction.CommitAsync(ct);
    }

    public const string AnonymizedName = "Cliente eliminado";

    private async Task ApplyAsync(Customer customer, CustomerUpsertRequest request, CancellationToken ct)
    {
        var number = Documents.Normalize(request.DocumentType, request.DocumentNumber)!;
        if (await db.Customers.AnyAsync(c =>
                c.DocumentType == request.DocumentType && c.DocumentNumber == number && c.Id != customer.Id, ct))
            throw new FieldValidationException(nameof(request.DocumentNumber), "Ya tienes un cliente con ese documento.");

        // la autorizacion de datos se pide a las personas y queda la fecha en que se dio
        var needsConsent = Documents.IsPerson(request.DocumentType);
        if (needsConsent && !request.DataConsent && customer.DataConsentAt is null)
            throw new FieldValidationException(nameof(request.DataConsent),
                "Confirma que el cliente autorizó el tratamiento de sus datos personales.");
        if (request.DataConsent && customer.DataConsentAt is null)
            customer.DataConsentAt = timeProvider.GetUtcNow().UtcDateTime.ToMicroseconds();

        customer.DocumentType = request.DocumentType;
        customer.DocumentNumber = number;
        customer.Name = request.Name.Trim();
        customer.Phone = CommonValidators.PhoneDigits(request.Phone);
        customer.Email = CommonValidators.Clean(request.Email)?.ToLowerInvariant();
        customer.Address = CommonValidators.Clean(request.Address);
        customer.Notes = CommonValidators.Clean(request.Notes);
    }

    private async Task<Customer> FindAsync(Guid id, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == id && !c.IsAnonymized, ct) ?? throw new NotFoundException("El cliente");

    private static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.DocumentType, c.DocumentNumber, c.Name, c.Phone, c.Email, c.Address, c.Notes, c.DataConsentAt, c.CreatedAt);
}
