using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.Services;
public sealed class TransferService(AppDbContext db)
{
    public Task<ReportPage<TransferSummary>> List(TransferFilter f, CancellationToken ct)
    {
        f.Validate(); var q = db.StockTransfers.AsNoTracking();
        if (f.SourceWarehouseId.HasValue) q = q.Where(t => t.SourceWarehouseId == f.SourceWarehouseId);
        if (f.DestinationWarehouseId.HasValue) q = q.Where(t => t.DestinationWarehouseId == f.DestinationWarehouseId);
        if (f.Status.HasValue) q = q.Where(t => t.Status == f.Status);
        if (f.Start.HasValue) q = q.Where(t => t.CreatedAt >= f.Start && t.CreatedAt < f.End);
        if (!string.IsNullOrWhiteSpace(f.Search)) q = q.Where(t => t.TransferNumber.Contains(f.Search.Trim()));
        return ReportingService.Page(q.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => new TransferSummary(t.Id, t.TransferNumber, t.SourceWarehouseId, t.SourceWarehouse.Name,
            t.DestinationWarehouseId, t.DestinationWarehouse.Name, t.Status, t.CreatedAt, t.CompletedAt, t.Items.Sum(i => (long)i.Quantity))), new() { Page = f.Page, PageSize = f.PageSize }, ct);
    }
    public Task<TransferResponse?> Get(Guid id, CancellationToken ct) => db.StockTransfers.AsNoTracking().Where(t => t.Id == id).Select(t => new TransferResponse(t.Id, t.TransferNumber,
        t.SourceWarehouseId, t.SourceWarehouse.Name, t.DestinationWarehouseId, t.DestinationWarehouse.Name, t.Status, t.CreatedAt, t.UpdatedAt, t.CompletedAt, t.CreatedByUserId,
        t.CreatedByUser.Name, t.CompletedByUserId, t.CompletedByUser == null ? null : t.CompletedByUser.Name, t.Notes, t.Items.OrderBy(i => i.Product.Name).ThenBy(i => i.Id)
            .Select(i => new TransferLine(i.Id, i.ProductId, i.Product.Name, i.Quantity, db.WarehouseInventories.Where(b => b.WarehouseId == t.SourceWarehouseId && b.ProductId == i.ProductId).Select(b => (int?)b.QuantityOnHand).FirstOrDefault() ?? 0)).ToList())).SingleOrDefaultAsync(ct);
    private Task<StockTransfer?> Lock(Guid id, CancellationToken ct) => db.StockTransfers.FromSqlInterpolated($"SELECT * FROM \"StockTransfers\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    public async Task<TransferResponse?> Save(Guid? id, TransferRequest input, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); var transfer = id is null ? new StockTransfer { CreatedByUserId = actor } : await Lock(id.Value, ct);
        if (transfer is null) return null; if (transfer.Status != TransferStatus.Draft) throw new BusinessException("transferNotDraft", 409);
        await InventoryService.LockWarehouses(db, [input.SourceWarehouseId, input.DestinationWarehouseId], ct);
        await InventoryService.LockProducts(db, input.Items.Select(i => i.ProductId), ct, requireActive: true);
        if (id is not null) await db.Entry(transfer).Collection(t => t.Items).LoadAsync(ct);
        else { var n = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"TransferNumbers\"') AS \"Value\"").SingleAsync(ct); transfer.TransferNumber = $"TR-{DateTime.UtcNow:yyyyMMdd}-{n:D6}"; db.StockTransfers.Add(transfer); }
        transfer.SourceWarehouseId = input.SourceWarehouseId; transfer.DestinationWarehouseId = input.DestinationWarehouseId; transfer.Notes = input.Notes.Trim();
        foreach (var removed in transfer.Items.Where(i => !input.Items.Any(x => x.ProductId == i.ProductId)).ToArray()) { db.StockTransferItems.Remove(removed); transfer.Items.Remove(removed); }
        foreach (var line in input.Items) { var item = transfer.Items.SingleOrDefault(i => i.ProductId == line.ProductId); if (item is null) { item = new() { ProductId = line.ProductId, StockTransferId = transfer.Id }; transfer.Items.Add(item); db.StockTransferItems.Add(item); } item.Quantity = line.Quantity; }
        if (id is not null) transfer.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Get(transfer.Id, ct);
    }
    public async Task<TransferResponse?> Transition(Guid id, bool complete, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); var transfer = await Lock(id, ct); if (transfer is null) return null;
        if (complete && transfer.Status == TransferStatus.Completed) return await Get(id, ct); // Same resource transition is idempotent.
        if (transfer.Status != TransferStatus.Draft) throw new BusinessException("invalidTransferTransition", 409);
        await db.Entry(transfer).Collection(t => t.Items).LoadAsync(ct);
        if (complete)
        {
            if (transfer.Items.Count == 0) throw new BusinessException("invalidTransferTransition", 409);
            await InventoryService.LockWarehouses(db, [transfer.SourceWarehouseId, transfer.DestinationWarehouseId], ct);
            var products = transfer.Items.Select(i => i.ProductId).Order().ToArray(); await InventoryService.LockProducts(db, products, ct, requireActive: true);
            var balances = new Dictionary<(Guid Warehouse, Guid Product), WarehouseInventory>();
            foreach (var warehouse in new[] { transfer.SourceWarehouseId, transfer.DestinationWarehouseId }.Order())
                foreach (var product in products) balances[(warehouse, product)] = await InventoryService.LockBalance(db, warehouse, product, ct);
            foreach (var i in transfer.Items)
                if (balances[(transfer.SourceWarehouseId, i.ProductId)].QuantityOnHand < i.Quantity) throw new BusinessException("insufficientStock", 409);
            foreach (var i in transfer.Items.OrderBy(i => i.ProductId))
            {
                InventoryService.Apply(db, balances[(transfer.SourceWarehouseId, i.ProductId)], i.Quantity, MovementType.TransferOut, actor, "Internal stock transfer", transferId: id);
                InventoryService.Apply(db, balances[(transfer.DestinationWarehouseId, i.ProductId)], i.Quantity, MovementType.TransferIn, actor, "Internal stock transfer", transferId: id);
            }
            transfer.Status = TransferStatus.Completed; transfer.CompletedAt = DateTimeOffset.UtcNow; transfer.CompletedByUserId = actor;
        }
        else transfer.Status = TransferStatus.Cancelled;
        transfer.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Get(id, ct);
    }
}
