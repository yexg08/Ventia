using Ventia.Api.Auth;
using Ventia.Application.Parties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ventia.Api.Controllers;

/// <summary>clientes (los cajeros los consultan y registran desde la caja y solo el dueno los borra)</summary>
[ApiController]
[Route("api/customers")]
public class CustomersController(ICustomersService customers) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CustomerDto>> List([FromQuery] string? search, CancellationToken ct) => customers.ListAsync(search, ct);

    [HttpGet("{id:guid}")]
    public Task<CustomerDto> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(CustomerUpsertRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await customers.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<CustomerDto> Update(Guid id, CustomerUpsertRequest request, CancellationToken ct) => customers.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.TenantOwner)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>proveedores (todo solo para el dueno)</summary>
[ApiController]
[Route("api/suppliers")]
[Authorize(Policy = Policies.TenantOwner)]
public class SuppliersController(ISuppliersService suppliers) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SupplierDto>> List([FromQuery] string? search, CancellationToken ct) => suppliers.ListAsync(search, ct);

    [HttpGet("{id:guid}")]
    public Task<SupplierDto> Get(Guid id, CancellationToken ct) => suppliers.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(SupplierUpsertRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await suppliers.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<SupplierDto> Update(Guid id, SupplierUpsertRequest request, CancellationToken ct) => suppliers.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await suppliers.DeleteAsync(id, ct);
        return NoContent();
    }
}
