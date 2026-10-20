using Ventia.Api.Auth;
using Ventia.Application.Purchasing;
using Ventia.Application.Sales;
using Ventia.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ventia.Api.Controllers;

/// <summary>compras a proveedores (todo solo para el dueno)</summary>
[ApiController]
[Route("api/purchases")]
[Authorize(Policy = Policies.TenantOwner)]
public class PurchasesController(IPurchasesService purchases) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PurchaseSummaryDto>> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] PurchaseStatus? status, CancellationToken ct) =>
        purchases.ListAsync(new PurchasesQuery(from, to, status), ct);

    [HttpGet("{id:guid}")]
    public Task<PurchaseDto> Get(Guid id, CancellationToken ct) => purchases.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<PurchaseDto>> Create(CreatePurchaseRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await purchases.CreateAsync(request, ct));

    [HttpPost("{id:guid}/void")]
    public Task<PurchaseDto> Void(Guid id, VoidPurchaseRequest request, CancellationToken ct) => purchases.VoidAsync(id, request, ct);
}

/// <summary>cotizaciones (cualquier miembro del negocio las hace y las convierte en venta)</summary>
[ApiController]
[Route("api/quotes")]
public class QuotesController(IQuotesService quotes) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<QuoteSummaryDto>> List([FromQuery] QuoteStatus? status, CancellationToken ct) => quotes.ListAsync(status, ct);

    [HttpGet("{id:guid}")]
    public Task<QuoteDto> Get(Guid id, CancellationToken ct) => quotes.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<QuoteDto>> Create(QuoteUpsertRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await quotes.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<QuoteDto> Update(Guid id, QuoteUpsertRequest request, CancellationToken ct) => quotes.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<QuoteDto> Cancel(Guid id, CancellationToken ct) => quotes.CancelAsync(id, ct);

    [HttpPost("{id:guid}/convert")]
    public async Task<ActionResult<SaleDto>> Convert(Guid id, ConvertQuoteRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await quotes.ConvertAsync(id, request, ct));
}
