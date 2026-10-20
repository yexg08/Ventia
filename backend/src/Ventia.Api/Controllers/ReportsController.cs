using Ventia.Api.Auth;
using Ventia.Application.Common;
using Ventia.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ventia.Api.Controllers;

/// <summary>reportes y exportaciones a CSV (solo el dueno)</summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = Policies.TenantOwner)]
public class ReportsController(IReportsService reports) : ControllerBase
{
    [HttpGet("sales")]
    public Task<SalesReportDto> Sales([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        reports.GetSalesAsync(new ReportQuery(from, to), ct);

    [HttpGet("inventory")]
    public Task<InventoryReportDto> Inventory(CancellationToken ct) => reports.GetInventoryAsync(ct);

    /// <param name="kind">sales products expenses purchases o inventory</param>
    [HttpGet("export/{kind}")]
    public async Task<IActionResult> Export(string kind, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        // solo por nombre (TryParse tambien aceptaria el numero 1)
        if (!kind.All(char.IsAsciiLetter) || !Enum.TryParse<ExportKind>(kind, ignoreCase: true, out var exportKind))
            throw new NotFoundException("El reporte");

        var file = await reports.ExportAsync(exportKind, new ReportQuery(from, to), ct);
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}
