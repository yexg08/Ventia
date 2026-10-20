using Ventia.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Common;

public enum DocumentKind
{
    Sale,
    Purchase,
    Quote
}

/// <summary>
/// consecutivos por negocio sin saltos ni repetidos
/// se llama DENTRO de la transaccion del documento (el UPDATE bloquea la fila del negocio hasta confirmar asi dos
/// documentos simultaneos nunca reciben el mismo numero y si el documento falla el numero vuelve atras)
/// </summary>
public static class DocumentNumbers
{
    public static async Task<int> NextAsync(IAppDbContext db, Guid tenantId, DocumentKind kind, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("El consecutivo solo se toma dentro de una transacción.");

        var tenant = db.Tenants.Where(t => t.Id == tenantId);
        switch (kind)
        {
            case DocumentKind.Sale:
                await tenant.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastSaleNumber, t => t.LastSaleNumber + 1), ct);
                return await tenant.Select(t => t.LastSaleNumber).FirstAsync(ct);
            case DocumentKind.Purchase:
                await tenant.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastPurchaseNumber, t => t.LastPurchaseNumber + 1), ct);
                return await tenant.Select(t => t.LastPurchaseNumber).FirstAsync(ct);
            default:
                await tenant.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastQuoteNumber, t => t.LastQuoteNumber + 1), ct);
                return await tenant.Select(t => t.LastQuoteNumber).FirstAsync(ct);
        }
    }
}
