using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Application.Inventory;

/// <summary>
/// unico punto por donde cambia el stock (los ajustes de hoy y las ventas y compras de las fases siguientes)
/// cada cambio es atomico en la base y queda registrado como movimiento
/// </summary>
public interface IStockService
{
    /// <summary>suma o resta unidades (falla con 409 si el producto no lleva inventario o si quedaria negativo)</summary>
    Task<StockMovement> ChangeAsync(Guid productId, int quantity, StockMovementType type, string? reason, CancellationToken ct);

    /// <summary>deja el stock en lo que se conto fisicamente y registra la diferencia</summary>
    Task<StockMovement> SetCountedAsync(Guid productId, int counted, string? reason, CancellationToken ct);
}

public class StockService(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IStockService
{
    public async Task<StockMovement> ChangeAsync(Guid productId, int quantity, StockMovementType type, string? reason, CancellationToken ct)
    {
        if (quantity == 0) throw new BusinessRuleException("La cantidad no puede ser cero.");

        return await InTransactionAsync(async () =>
        {
            // un solo UPDATE con la condicion adentro (si llegan dos salidas a la vez la base las pone en fila y la
            // segunda vuelve a evaluar la condicion con el stock ya descontado asi que nunca se vende lo que no hay)
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var updated = await db.Products
                .Where(p => p.Id == productId && p.TracksStock && p.Stock + quantity >= 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Stock, p => p.Stock + quantity)
                    .SetProperty(p => p.UpdatedAt, now), ct);

            if (updated == 0) await ThrowWhyNotAsync(productId, ct);
            return await RecordAsync(productId, quantity, type, reason, ct);
        }, ct);
    }

    public async Task<StockMovement> SetCountedAsync(Guid productId, int counted, string? reason, CancellationToken ct)
    {
        if (counted < 0) throw new BusinessRuleException("El conteo no puede ser negativo.");

        return await InTransactionAsync(async () =>
        {
            var current = await db.Products.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => new { p.Stock, p.TracksStock, p.Name })
                .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("El producto");
            if (!current.TracksStock) throw new BusinessRuleException($"\"{current.Name}\" no lleva inventario.");

            // solo cambia si nadie movio el stock entre la lectura y la escritura
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var updated = await db.Products
                .Where(p => p.Id == productId && p.Stock == current.Stock)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Stock, counted)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 0)
                throw new BusinessRuleException("El stock cambió mientras registrabas el conteo. Revisa y vuelve a intentarlo.");

            return await RecordAsync(productId, counted - current.Stock, StockMovementType.Count, reason, ct);
        }, ct);
    }

    private async Task<StockMovement> RecordAsync(Guid productId, int quantity, StockMovementType type, string? reason, CancellationToken ct)
    {
        var stockAfter = await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).FirstAsync(ct);
        var movement = new StockMovement
        {
            ProductId = productId,
            Type = type,
            Quantity = quantity,
            StockAfter = stockAfter,
            Reason = CommonValidators.Clean(reason),
            UserId = currentUser.UserId,
            UserName = currentUser.Name ?? "Sistema"
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        return movement;
    }

    // el UPDATE no cambio nada y se explica por que (el filtro global hace que un producto ajeno no exista)
    private async Task ThrowWhyNotAsync(Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.TracksStock, p.Stock, p.Name })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("El producto");
        if (!product.TracksStock) throw new BusinessRuleException($"\"{product.Name}\" no lleva inventario.");
        throw new BusinessRuleException($"No hay suficiente stock de \"{product.Name}\" (quedan {product.Stock}).");
    }

    // usa la transaccion de quien llama si ya hay una (asi una venta descuenta todo o nada)
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await work();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await work();
        await transaction.CommitAsync(ct);
        return result;
    }
}
