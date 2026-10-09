using System.Linq.Expressions;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Services;

public sealed class InventoryService(AppDbContext db)
{
    private static readonly Expression<Func<Inventory, InventoryResponse>> Projection = i => new(i.Id, i.ProductId,
        i.Product.Name, i.Product.Category.Name, i.Product.IsActive, i.QuantityOnHand, i.MinimumStockLevel, i.UpdatedAt);
    public Task<List<InventoryResponse>> List(CancellationToken ct) => db.Inventories.AsNoTracking().OrderBy(i => i.Product.Name).Select(Projection).ToListAsync(ct);
    public Task<InventoryResponse?> Get(Guid productId, CancellationToken ct) => db.Inventories.AsNoTracking().Where(i => i.ProductId == productId).Select(Projection).SingleOrDefaultAsync(ct);
    public Task<List<MovementResponse>> Movements(Guid productId, CancellationToken ct) => db.InventoryMovements.AsNoTracking()
        .Where(m => m.ProductId == productId).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
        .Select(m => new MovementResponse(m.Id, m.ProductId, m.MovementType, m.Quantity, m.QuantityBefore, m.QuantityAfter,
            m.ReferenceType, m.ReferenceId, m.ReferenceOrder == null ? null : m.ReferenceOrder.OrderNumber,
            m.Reason, m.CreatedAt, m.CreatedByUserId, m.CreatedByUser.Name)).ToListAsync(ct);

    // All writers use these locks inside a ReadCommitted transaction. Multiple rows
    // must be locked sequentially in Guid order. Values are read AFTER acquiring locks.
    internal static Task<Inventory?> Lock(AppDbContext db, Guid productId, CancellationToken ct) => db.Inventories
        .FromSqlInterpolated($"SELECT * FROM \"Inventories\" WHERE \"ProductId\" = {productId} FOR UPDATE")
        .SingleOrDefaultAsync(ct);

    internal static void Apply(AppDbContext db, Inventory inventory, long quantity, MovementType type, Guid userId, string reason, Guid? orderId = null)
    {
        if (quantity <= 0 || quantity > int.MaxValue) throw new BusinessException("stockLimit", 409);
        var increase = type is MovementType.StockIn or MovementType.AdjustmentIncrease or MovementType.OrderCancellationReturn;
        var after = inventory.QuantityOnHand + (increase ? quantity : -quantity);
        if (after < 0) throw new BusinessException("insufficientStock", 409);
        if (after > int.MaxValue) throw new BusinessException("stockLimit", 409);
        db.InventoryMovements.Add(new InventoryMovement
        {
            ProductId = inventory.ProductId, MovementType = type, Quantity = (int)quantity,
            QuantityBefore = inventory.QuantityOnHand, QuantityAfter = (int)after,
            ReferenceType = orderId is null ? "Manual" : "Order", ReferenceId = orderId,
            Reason = reason.Trim(), CreatedByUserId = userId
        });
        inventory.QuantityOnHand = (int)after;
        inventory.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task<InventoryResponse> Change(Guid productId, int quantity, MovementType type, Guid userId, string reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inventory = await Lock(db, productId, ct) ?? throw new BusinessException("inventoryNotFound", 404);
        Apply(db, inventory, quantity, type, userId, reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await Get(productId, ct))!;
    }

    public async Task<InventoryResponse> Minimum(Guid productId, int level, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inventory = await Lock(db, productId, ct) ?? throw new BusinessException("inventoryNotFound", 404);
        inventory.MinimumStockLevel = level;
        inventory.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await Get(productId, ct))!;
    }
}
