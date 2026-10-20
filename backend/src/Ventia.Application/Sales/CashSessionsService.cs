using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Sales;

public interface ICashSessionsService
{
    /// <summary>turno abierto de la cuenta de la sesion (null si no tiene)</summary>
    Task<CashSessionDto?> GetCurrentAsync(CancellationToken ct);
    Task<CashSessionDto> OpenAsync(OpenCashSessionRequest request, CancellationToken ct);
    Task<CashSessionDto> CloseCurrentAsync(CloseCashSessionRequest request, CancellationToken ct);

    /// <summary>ultimos turnos de todo el negocio (solo el dueno)</summary>
    Task<IReadOnlyList<CashSessionDto>> ListAsync(CancellationToken ct);

    /// <summary>turno abierto de la cuenta (para registrar una venta) o 409 si no hay</summary>
    Task<CashSession> RequireOpenAsync(CancellationToken ct);
}

public class CashSessionsService(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : ICashSessionsService
{
    public async Task<CashSessionDto?> GetCurrentAsync(CancellationToken ct)
    {
        var session = await FindOpenAsync(tracked: false, ct);
        return session is null ? null : await ToDtoAsync(session, ct);
    }

    public async Task<CashSessionDto> OpenAsync(OpenCashSessionRequest request, CancellationToken ct)
    {
        if (await FindOpenAsync(tracked: false, ct) is not null)
            throw new BusinessRuleException("Ya tienes una caja abierta. Ciérrala antes de abrir otra.");

        var session = new CashSession
        {
            UserId = RequireUserId(),
            UserName = currentUser.Name ?? "Cajero",
            OpeningCash = request.OpeningCash
        };
        db.CashSessions.Add(session);
        // el indice unico parcial impide dos turnos abiertos de la misma cuenta aunque lleguen al mismo tiempo
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(session, ct);
    }

    public async Task<CashSessionDto> CloseCurrentAsync(CloseCashSessionRequest request, CancellationToken ct)
    {
        var session = await FindOpenAsync(tracked: true, ct) ?? throw new BusinessRuleException("No tienes una caja abierta.");

        session.ExpectedCash = await ExpectedCashAsync(session, ct);
        session.CountedCash = request.CountedCash;
        session.CloseNotes = CommonValidators.Clean(request.Notes);
        session.ClosedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(session, ct);
    }

    public async Task<IReadOnlyList<CashSessionDto>> ListAsync(CancellationToken ct)
    {
        var sessions = await db.CashSessions.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(50).ToListAsync(ct);
        var result = new List<CashSessionDto>(sessions.Count);
        foreach (var session in sessions) result.Add(await ToDtoAsync(session, ct));
        return result;
    }

    public async Task<CashSession> RequireOpenAsync(CancellationToken ct) =>
        await FindOpenAsync(tracked: false, ct)
        ?? throw new BusinessRuleException("Abre tu caja antes de vender.");

    private Task<CashSession?> FindOpenAsync(bool tracked, CancellationToken ct)
    {
        var userId = RequireUserId();
        var query = tracked ? db.CashSessions : db.CashSessions.AsNoTracking();
        return query.FirstOrDefaultAsync(s => s.UserId == userId && s.ClosedAt == null, ct);
    }

    private Guid RequireUserId() => currentUser.UserId ?? throw new UnauthorizedAccessException();

    // base mas lo cobrado en efectivo en ventas no anuladas menos lo que salio de la caja
    private async Task<decimal> ExpectedCashAsync(CashSession session, CancellationToken ct) =>
        session.OpeningCash
        + await db.SalePayments
            .Where(p => p.Method == PaymentMethod.Cash
                && db.Sales.Any(s => s.Id == p.SaleId && s.CashSessionId == session.Id && s.Status == SaleStatus.Completed))
            .SumAsync(p => p.Amount, ct)
        - await CashOutAsync(session, ct);

    // gastos y pagos a proveedores en efectivo que salieron de este turno
    private async Task<decimal> CashOutAsync(CashSession session, CancellationToken ct) =>
        await db.Expenses.Where(e => e.CashSessionId == session.Id && !e.IsVoided).SumAsync(e => e.Amount, ct)
        + await db.SupplierPayments.Where(p => p.CashSessionId == session.Id && !p.IsVoided).SumAsync(p => p.Amount, ct);

    private async Task<CashSessionDto> ToDtoAsync(CashSession session, CancellationToken ct)
    {
        var sales = db.Sales.Where(s => s.CashSessionId == session.Id);
        var completed = sales.Where(s => s.Status == SaleStatus.Completed);
        var byMethod = await db.SalePayments
            .Where(p => completed.Any(s => s.Id == p.SaleId))
            .GroupBy(p => p.Method)
            .Select(g => new PaymentSummaryDto(g.Key, g.Sum(p => p.Amount)))
            .ToListAsync(ct);
        var expected = session.ExpectedCash ?? await ExpectedCashAsync(session, ct);

        return new CashSessionDto(
            session.Id, session.UserName, session.CreatedAt, session.OpeningCash, session.ClosedAt,
            await completed.CountAsync(ct), await sales.CountAsync(s => s.Status == SaleStatus.Voided, ct),
            await completed.SumAsync(s => s.Total, ct), byMethod.OrderBy(m => m.Method).ToList(),
            await CashOutAsync(session, ct), expected, session.CountedCash, session.CountedCash - session.ExpectedCash, session.CloseNotes);
    }
}
