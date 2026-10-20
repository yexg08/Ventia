using Ventia.Api.Auth;
using Ventia.Application.Business;
using Ventia.Application.Catalog;
using Ventia.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ventia.Api.Controllers;

/// <summary>ajustes del negocio de la sesion (sin atributo de clase aplica la politica de respaldo de miembro del negocio)</summary>
[ApiController]
[Route("api/business")]
public class BusinessController(IBusinessService business) : ControllerBase
{
    [HttpGet]
    public Task<BusinessDto> Get(CancellationToken ct) => business.GetCurrentAsync(ct);

    [HttpPut]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<BusinessDto> Update(UpdateBusinessRequest request, CancellationToken ct) => business.UpdateCurrentAsync(request, ct);
}

/// <summary>categorias del menu (los cajeros las ven y solo el dueno las gestiona)</summary>
[ApiController]
[Route("api/categories")]
public class CategoriesController(ICategoriesService categories) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => categories.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<CategoryDto> Get(Guid id, CancellationToken ct) => categories.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.TenantOwner)]
    public async Task<ActionResult<CategoryDto>> Create(CategoryUpsertRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await categories.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.TenantOwner)]
    public Task<CategoryDto> Update(Guid id, CategoryUpsertRequest request, CancellationToken ct) =>
        categories.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.TenantOwner)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>cuentas del negocio (todo solo para el dueno)</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.TenantOwner)]
public class UsersController(IUsersService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserAccountDto>> List(CancellationToken ct) => users.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<TemporaryPasswordDto>> CreateCashier(CreateCashierRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await users.CreateCashierAsync(request, ct));

    [HttpPost("{id:guid}/reset-password")]
    public Task<TemporaryPasswordDto> ResetPassword(Guid id, CancellationToken ct) => users.ResetPasswordAsync(id, ct);

    [HttpPatch("{id:guid}/status")]
    public Task<UserAccountDto> SetStatus(Guid id, SetUserStatusRequest request, CancellationToken ct) =>
        users.SetActiveAsync(id, request.IsActive, ct);
}
