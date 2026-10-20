using Ventia.Api.Auth;
using Ventia.Application.Sales;
using Ventia.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ventia.Api.Controllers;

/// <summary>turnos de caja (cada cuenta abre y cierra el suyo y el dueno ve los de todos)</summary>
[ApiController]
[Route("api/cash-sessions")]
public class CashSessionsController(ICashSessionsService sessions) : ControllerBase
{
    /// <summary>turno abierto de la cuenta o 204 si no tiene</summary>
    [HttpGet("current")]
    public async Task<ActionResult<CashSessionDto>> Current(CancellationToken ct) =>
        await sessions.GetCurrentAsync(ct) is { } current ? current : NoContent();

    [HttpPost]
    public async Task<ActionResult<CashSessionDto>> Open(OpenCashSessionRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await sessions.OpenAsync(request, ct));

    [HttpPost("current/close")]
    public Task<CashSessionDto> Close(CloseCashSessionRequest request, CancellationToken ct) => sessions.CloseCurrentAsync(request, ct);

    [HttpGet]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<IReadOnlyList<CashSessionDto>> List(CancellationToken ct) => sessions.ListAsync(ct);
}

/// <summary>ventas (cualquier miembro vende y ve las suyas y solo el dueno ve todas y anula)</summary>
[ApiController]
[Route("api/sales")]
public class SalesController(ISalesService sales) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SaleDto>> Create(CreateSaleRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await sales.CreateAsync(request, ct));

    [HttpGet]
    public Task<IReadOnlyList<SaleSummaryDto>> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] SaleStatus? status, CancellationToken ct) =>
        sales.ListAsync(new SalesQuery(from, to, status), ct);

    [HttpGet("{id:guid}")]
    public Task<SaleDto> Get(Guid id, CancellationToken ct) => sales.GetAsync(id, ct);

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<SaleDto> Void(Guid id, VoidSaleRequest request, CancellationToken ct) => sales.VoidAsync(id, request, ct);
}
