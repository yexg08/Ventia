using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Application.Sales;
using Ventia.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Expenses;

/// <param name="FromCashRegister">el efectivo sale de la caja abierta de quien lo registra</param>
public record CreateExpenseRequest(
    DateOnly? Date, ExpenseCategory Category, string Description, decimal Amount, PaymentMethod Method, bool FromCashRegister);

public record VoidExpenseRequest(string Reason);

public record ExpenseDto(
    Guid Id, DateOnly Date, ExpenseCategory Category, string Description, decimal Amount, PaymentMethod Method,
    bool FromCashRegister, string CreatedByName, DateTime CreatedAt, bool IsVoided, string? VoidedBy, string? VoidReason);

public record ExpensesQuery(DateOnly? From, DateOnly? To, ExpenseCategory? Category);

public class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.Category).IsInEnum().WithMessage("La categoría no es válida.");
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Describe el gasto.")
            .MaximumLength(150).WithMessage("La descripción no puede superar 150 caracteres.");
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("El valor debe ser mayor que cero.")
            .LessThanOrEqualTo(1_000_000_000).WithMessage("El valor es demasiado alto.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("El valor admite máximo 2 decimales.");
        RuleFor(x => x.Method).IsInEnum().WithMessage("El medio de pago no es válido.");
        RuleFor(x => x.FromCashRegister)
            .Must((request, fromRegister) => !fromRegister || request.Method == PaymentMethod.Cash)
            .WithMessage("Solo el efectivo puede salir de la caja.");
    }
}

public class VoidExpenseRequestValidator : AbstractValidator<VoidExpenseRequest>
{
    public VoidExpenseRequestValidator() =>
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Escribe el motivo de la anulación.")
            .MaximumLength(200).WithMessage("El motivo no puede superar 200 caracteres.");
}

public interface IExpensesService
{
    Task<IReadOnlyList<ExpenseDto>> ListAsync(ExpensesQuery query, CancellationToken ct);
    Task<ExpenseDto> CreateAsync(CreateExpenseRequest request, CancellationToken ct);
    Task<ExpenseDto> VoidAsync(Guid id, VoidExpenseRequest request, CancellationToken ct);
}

/// <summary>
/// gastos del negocio de la sesion
/// el dueno registra y ve todos y el cajero solo registra salidas de su caja y ve las suyas
/// </summary>
public class ExpensesService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ICashSessionsService cashSessions,
    TimeProvider timeProvider) : IExpensesService
{
    public async Task<IReadOnlyList<ExpenseDto>> ListAsync(ExpensesQuery query, CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        var to = query.To ?? today;
        var from = query.From ?? new DateOnly(to.Year, to.Month, 1);
        if (from > to) (from, to) = (to, from);

        var expenses = Visible().AsNoTracking().Where(e => e.Date >= from && e.Date <= to);
        if (query.Category is { } category) expenses = expenses.Where(e => e.Category == category);
        return await expenses
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
            .Take(500)
            .Select(e => ToDto(e))
            .ToListAsync(ct);
    }

    public async Task<ExpenseDto> CreateAsync(CreateExpenseRequest request, CancellationToken ct)
    {
        // el cajero solo puede sacar efectivo de su propia caja (para pagar un domicilio o una compra menor)
        if (!currentUser.IsOwner && !request.FromCashRegister)
            throw new BusinessRuleException("Como cajero solo puedes registrar salidas de efectivo de tu caja.");

        var today = await TodayAsync(ct);
        var date = request.Date ?? today;
        if (date > today) throw new FieldValidationException(nameof(request.Date), "La fecha del gasto no puede ser futura.");

        Guid? sessionId = null;
        if (request.FromCashRegister)
        {
            if (date != today) throw new FieldValidationException(nameof(request.Date), "Una salida de caja es de hoy.");
            sessionId = (await cashSessions.RequireOpenAsync(ct)).Id;
        }

        var expense = new Expense
        {
            Date = date,
            Category = request.Category,
            Description = request.Description.Trim(),
            Amount = request.Amount,
            Method = request.Method,
            CashSessionId = sessionId,
            CreatedById = currentUser.UserId ?? throw new UnauthorizedAccessException(),
            CreatedByName = currentUser.Name ?? "Equipo"
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return ToDto(expense);
    }

    public async Task<ExpenseDto> VoidAsync(Guid id, VoidExpenseRequest request, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("El gasto");
        if (expense.IsVoided) throw new BusinessRuleException("El gasto ya está anulado.");

        expense.IsVoided = true;
        expense.VoidedAt = timeProvider.GetUtcNow().UtcDateTime;
        expense.VoidedBy = currentUser.Name ?? "Dueño";
        expense.VoidReason = request.Reason.Trim();
        await db.SaveChangesAsync(ct);
        return ToDto(expense);
    }

    private IQueryable<Expense> Visible() =>
        currentUser.IsOwner ? db.Expenses : db.Expenses.Where(e => e.CreatedById == currentUser.UserId);

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var zone = BusinessTime.Zone(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(ct));
        return BusinessTime.Today(zone, timeProvider);
    }

    private static ExpenseDto ToDto(Expense e) =>
        new(e.Id, e.Date, e.Category, e.Description, e.Amount, e.Method, e.CashSessionId is not null, e.CreatedByName,
            e.CreatedAt, e.IsVoided, e.VoidedBy, e.VoidReason);
}
