using Ventia.Api.Auth;
using Ventia.Application.Expenses;
using Ventia.Application.Purchasing;
using Ventia.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ventia.Api.Controllers;

/// <summary>gastos (el cajero solo registra salidas de su caja y ve las suyas y solo el dueno anula)</summary>
[ApiController]
[Route("api/expenses")]
public class ExpensesController(IExpensesService expenses) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ExpenseDto>> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] ExpenseCategory? category, CancellationToken ct) =>
        expenses.ListAsync(new ExpensesQuery(from, to, category), ct);

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create(CreateExpenseRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await expenses.CreateAsync(request, ct));

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<ExpenseDto> Void(Guid id, VoidExpenseRequest request, CancellationToken ct) => expenses.VoidAsync(id, request, ct);
}

/// <summary>cuentas por pagar y pagos a proveedores (todo solo para el dueno)</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = Policies.TenantOwner)]
public class SupplierPaymentsController(ISupplierPaymentsService payments) : ControllerBase
{
    [HttpGet("payables")]
    public Task<IReadOnlyList<PayableDto>> Payables(CancellationToken ct) => payments.ListPayablesAsync(ct);

    [HttpPost("purchases/{id:guid}/payments")]
    public async Task<ActionResult<SupplierPaymentDto>> Pay(Guid id, PaySupplierRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await payments.PayAsync(id, request, ct));

    [HttpPost("supplier-payments/{id:guid}/void")]
    public Task<SupplierPaymentDto> Void(Guid id, VoidSupplierPaymentRequest request, CancellationToken ct) =>
        payments.VoidAsync(id, request, ct);
}
