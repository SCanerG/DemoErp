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
            m.Reason, m.CreatedAt, m.CreatedByUserId, m.CreatedByUser.Name, m.GoodsReceiptId, m.ReferenceReceipt == null ? null : m.ReferenceReceipt.ReceiptNumber, m.WarehouseId, m.Warehouse.Name, m.StockTransferId, m.StockTransfer == null ? null : m.StockTransfer.TransferNumber, m.ProductionOrderId, m.ProductionOrder == null ? null : m.ProductionOrder.ProductionOrderNumber)).ToListAsync(ct);

    internal static async Task<Guid> ResolveWarehouse(AppDbContext db, Guid? warehouseId, CancellationToken ct) => warehouseId
        ?? await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).SingleAsync(ct);
    internal static async Task LockWarehouses(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct, bool requireActive = true)
    {
        foreach (var id in ids.Distinct().Order())
        {
            var w = await db.Warehouses.FromSqlInterpolated($"SELECT * FROM \"Warehouses\" WHERE \"Id\" = {id} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (w is null || requireActive && !w.IsActive) throw new BusinessException("warehouseUnavailable", 409);
        }
    }
    internal static async Task LockProducts(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct, bool requireActive = false)
    {
        foreach (var id in ids.Distinct().Order())
        {
            var p = await db.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (p is null || requireActive && !p.IsActive) throw new BusinessException("productUnavailable", 409);
        }
    }
    // Callers first lock all warehouses, then sorted products, then sorted warehouse/product balances.
    internal static async Task<WarehouseInventory> LockBalance(AppDbContext db, Guid warehouseId, Guid productId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"WarehouseInventories\" (\"Id\",\"WarehouseId\",\"ProductId\",\"QuantityOnHand\",\"MinimumStockLevel\",\"UpdatedAt\") VALUES ({Guid.NewGuid()},{warehouseId},{productId},0,0,CURRENT_TIMESTAMP) ON CONFLICT (\"WarehouseId\",\"ProductId\") DO NOTHING", ct);
        return await db.WarehouseInventories.FromSqlInterpolated($"SELECT * FROM \"WarehouseInventories\" WHERE \"WarehouseId\" = {warehouseId} AND \"ProductId\" = {productId} FOR UPDATE").SingleAsync(ct);
    }
    internal static async Task<WarehouseInventory?> Lock(AppDbContext db, Guid productId, CancellationToken ct, Guid? warehouseId = null)
    {
        var warehouse = await ResolveWarehouse(db, warehouseId, ct); await LockWarehouses(db, [warehouse], ct);
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) return null;
        await LockProducts(db, [productId], ct); return await LockBalance(db, warehouse, productId, ct);
    }

    internal static void Apply(AppDbContext db, WarehouseInventory inventory, long quantity, MovementType type, Guid userId, string reason, Guid? orderId = null, Guid? receiptId = null, Guid? transferId = null, Guid? productionId = null)
    {
        if (quantity <= 0 || quantity > int.MaxValue) throw new BusinessException("stockLimit", 409);
        var increase = type is MovementType.StockIn or MovementType.AdjustmentIncrease or MovementType.OrderCancellationReturn or MovementType.PurchaseReceipt or MovementType.TransferIn or MovementType.ProductionOutput;
        var after = inventory.QuantityOnHand + (increase ? quantity : -quantity);
        if (after < 0) throw new BusinessException("insufficientStock", 409);
        if (after > int.MaxValue) throw new BusinessException("stockLimit", 409);
        db.InventoryMovements.Add(new InventoryMovement
        {
            ProductionOrderId = productionId, WarehouseId = inventory.WarehouseId, StockTransferId = transferId, ProductId = inventory.ProductId, MovementType = type, Quantity = (int)quantity,
            QuantityBefore = inventory.QuantityOnHand, QuantityAfter = (int)after,
            ReferenceType = productionId is not null ? "Production" : transferId is not null ? "Transfer" : receiptId is not null ? "Receipt" : orderId is null ? "Manual" : "Order", ReferenceId = orderId, GoodsReceiptId = receiptId,
            Reason = reason.Trim(), CreatedByUserId = userId
        });
        inventory.QuantityOnHand = (int)after;
        inventory.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task<InventoryResponse> Change(Guid productId, int quantity, MovementType type, Guid userId, string reason, CancellationToken ct, Guid? warehouseId = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inventory = await Lock(db, productId, ct, warehouseId) ?? throw new BusinessException("inventoryNotFound", 404);
        Apply(db, inventory, quantity, type, userId, reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await Get(productId, ct))!;
    }

    public async Task<InventoryResponse> Minimum(Guid productId, int level, CancellationToken ct, Guid? warehouseId = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inventory = await Lock(db, productId, ct, warehouseId) ?? throw new BusinessException("inventoryNotFound", 404);
        inventory.MinimumStockLevel = level;
        inventory.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await Get(productId, ct))!;
    }
}
