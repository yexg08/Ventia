using Ventia.Api.Auth;
using Ventia.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ventia.Api.Controllers;

/// <summary>productos e inventario (los cajeros consultan sin ver costos y solo el dueno gestiona y ajusta stock)</summary>
[ApiController]
[Route("api/products")]
public class ProductsController(IProductsService products) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProductDto>> List(
        [FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] bool lowStock, CancellationToken ct) =>
        products.ListAsync(new ProductQuery(search, categoryId, lowStock), ct);

    [HttpGet("{id:guid}")]
    public Task<ProductDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.TenantOwner)]
    public async Task<ActionResult<ProductDto>> Create(ProductUpsertRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await products.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<ProductDto> Update(Guid id, ProductUpsertRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.TenantOwner)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/stock-adjustments")]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<ProductDto> AdjustStock(Guid id, StockAdjustmentRequest request, CancellationToken ct) =>
        products.AdjustStockAsync(id, request, ct);

    [HttpGet("{id:guid}/movements")]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<IReadOnlyList<StockMovementDto>> Movements(Guid id, CancellationToken ct) => products.ListMovementsAsync(id, ct);
}
