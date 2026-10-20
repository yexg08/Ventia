using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Business;

public record BusinessDto(
    Guid Id, string Name, string? TaxId, string? Address, string? City, string? Phone, string TimeZone, string Currency,
    TaxType DefaultTax, string? ReceiptFooter);

public record UpdateBusinessRequest(
    string Name, string? TaxId, string? Address, string? City, string? Phone, string TimeZone, TaxType DefaultTax,
    string? ReceiptFooter);

public class UpdateBusinessRequestValidator : AbstractValidator<UpdateBusinessRequest>
{
    public UpdateBusinessRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(80).WithMessage("El nombre no puede superar 80 caracteres.");
        RuleFor(x => x.TaxId).OptionalNit();
        RuleFor(x => x.Address).MaximumLength(120).WithMessage("La dirección no puede superar 120 caracteres.");
        RuleFor(x => x.City).MaximumLength(60).WithMessage("La ciudad no puede superar 60 caracteres.");
        RuleFor(x => x.Phone).OptionalPhone();
        RuleFor(x => x.TimeZone)
            .Must(tz => tz is not null && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            .WithMessage("La zona horaria no es válida.");
        RuleFor(x => x.DefaultTax).IsInEnum().WithMessage("El impuesto no es válido.");
        RuleFor(x => x.ReceiptFooter).MaximumLength(200).WithMessage("El pie del comprobante no puede superar 200 caracteres.");
    }
}

public interface IBusinessService
{
    Task<BusinessDto> GetCurrentAsync(CancellationToken ct);
    Task<BusinessDto> UpdateCurrentAsync(UpdateBusinessRequest request, CancellationToken ct);
}

/// <summary>ajustes del negocio de la sesion (siempre opera sobre el TenantId de la sesion y nunca sobre uno recibido)</summary>
public class BusinessService(IAppDbContext db, ITenantContext tenant) : IBusinessService
{
    public async Task<BusinessDto> GetCurrentAsync(CancellationToken ct) => ToDto(await FindAsync(tracked: false, ct));

    public async Task<BusinessDto> UpdateCurrentAsync(UpdateBusinessRequest request, CancellationToken ct)
    {
        var t = await FindAsync(tracked: true, ct);

        t.Name = request.Name.Trim();
        t.TaxId = Nit.TryNormalize(request.TaxId, out var nit) ? nit : null;
        t.Address = CommonValidators.Clean(request.Address);
        t.City = CommonValidators.Clean(request.City);
        t.Phone = CommonValidators.PhoneDigits(request.Phone);
        t.TimeZone = request.TimeZone;
        t.DefaultTax = request.DefaultTax;
        t.ReceiptFooter = CommonValidators.Clean(request.ReceiptFooter);
        await db.SaveChangesAsync(ct);

        return ToDto(t);
    }

    private async Task<Tenant> FindAsync(bool tracked, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var query = tracked ? db.Tenants : db.Tenants.AsNoTracking();
        return await query.FirstOrDefaultAsync(x => x.Id == tenantId, ct) ?? throw new NotFoundException("El negocio");
    }

    private static BusinessDto ToDto(Tenant t) =>
        new(t.Id, t.Name, t.TaxId, t.Address, t.City, t.Phone, t.TimeZone, t.Currency, t.DefaultTax, t.ReceiptFooter);
}
