using System.Buffers.Binary;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Demo.Api.Services;

public sealed class SupplierService(AppDbContext db)
{
    private static readonly Expression<Func<Supplier, SupplierResponse>> Projection = s => new(s.Id, s.SupplierCode, s.Name, s.ContactPerson, s.Email, s.Phone, s.Address, s.TaxNumber, s.IsActive, s.CreatedAt, s.UpdatedAt);
    public Task<SupplierResponse?> Get(Guid id, CancellationToken ct) => db.Suppliers.AsNoTracking().Where(s => s.Id == id).Select(Projection).SingleOrDefaultAsync(ct);
    public Task<ReportPage<SupplierResponse>> List(SupplierFilter filter, CancellationToken ct)
    {
        filter.Validate(); var query = db.Suppliers.AsNoTracking();
        if (filter.IsActive.HasValue) query = query.Where(s => s.IsActive == filter.IsActive);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var search = filter.Search.Trim(); query = query.Where(s => s.Name.Contains(search) || s.SupplierCode.Contains(search.ToUpper())); }
        var sorted = filter.Sort == "supplierCode" ? filter.Descending ? query.OrderByDescending(s => s.SupplierCode) : query.OrderBy(s => s.SupplierCode)
            : filter.Descending ? query.OrderByDescending(s => s.Name) : query.OrderBy(s => s.Name);
        return ReportingService.Page(sorted.ThenBy(s => s.Id).Select(Projection), new() { Page = filter.Page, PageSize = filter.PageSize }, ct);
    }
    public async Task<SupplierResponse?> Save(Guid? id, SupplierRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var supplier = id is null ? new Supplier() : await db.Suppliers.FromSqlInterpolated($"SELECT * FROM \"Suppliers\" WHERE \"Id\" = {id.Value} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (supplier is null) return null;
        supplier.SupplierCode = input.SupplierCode.Trim().ToUpperInvariant(); supplier.Name = input.Name.Trim(); supplier.ContactPerson = input.ContactPerson.Trim();
        supplier.Email = input.Email.Trim().ToLowerInvariant(); supplier.Phone = input.Phone.Trim(); supplier.Address = input.Address.Trim(); supplier.TaxNumber = input.TaxNumber.Trim();
        if (id is null) db.Suppliers.Add(supplier); else supplier.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { throw new BusinessException("duplicateSupplierCode", 409); }
        await tx.CommitAsync(ct); return await Get(supplier.Id, ct);
    }
    public async Task<SupplierResponse?> Status(Guid id, bool active, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var supplier = await db.Suppliers.FromSqlInterpolated($"SELECT * FROM \"Suppliers\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (supplier is null) return null;
        if (supplier.IsActive != active) { supplier.IsActive = active; supplier.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct); return await Get(id, ct);
    }
}

public sealed class ProcurementService(AppDbContext db)
{
    public async Task<ProcurementSummary> Summary(CancellationToken ct)
    {
        var counts = await db.PurchaseOrders.AsNoTracking().GroupBy(o => 1).Select(g => new {
            Open = g.Count(o => o.Status == PurchaseOrderStatus.Submitted || o.Status == PurchaseOrderStatus.PartiallyReceived),
            Partial = g.Count(o => o.Status == PurchaseOrderStatus.PartiallyReceived), Received = g.Count(o => o.Status == PurchaseOrderStatus.Received)
        }).SingleOrDefaultAsync(ct);
        var active = await db.Suppliers.AsNoTracking().CountAsync(s => s.IsActive, ct);
        var outstanding = await db.PurchaseOrderItems.AsNoTracking().Where(i => i.PurchaseOrder.Status == PurchaseOrderStatus.Submitted || i.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived)
            .SumAsync(i => (long)i.QuantityOrdered - i.QuantityReceived, ct);
        return new(counts?.Open ?? 0, counts?.Partial ?? 0, counts?.Received ?? 0, active, outstanding);
    }
    internal static IQueryable<PurchaseSummary> Rows(AppDbContext db, PurchaseFilter filter)
    {
        filter.Validate(); var q = db.PurchaseOrders.AsNoTracking();
        if (filter.SupplierId.HasValue) q = q.Where(o => o.SupplierId == filter.SupplierId);
        if (filter.Status.HasValue) q = q.Where(o => o.Status == filter.Status);
        if (filter.Start.HasValue) q = q.Where(o => o.OrderDate >= filter.Start && o.OrderDate < filter.End);
        if (!string.IsNullOrWhiteSpace(filter.Search)) q = q.Where(o => o.PurchaseOrderNumber.Contains(filter.Search.Trim()));
        var sorted = filter.Sort switch {
            "totalAmount" => filter.Descending ? q.OrderByDescending(o => o.TotalAmount) : q.OrderBy(o => o.TotalAmount),
            "supplier" => filter.Descending ? q.OrderByDescending(o => o.Supplier.Name) : q.OrderBy(o => o.Supplier.Name),
            "purchaseOrderNumber" => filter.Descending ? q.OrderByDescending(o => o.PurchaseOrderNumber) : q.OrderBy(o => o.PurchaseOrderNumber),
            _ => filter.Descending ? q.OrderByDescending(o => o.OrderDate) : q.OrderBy(o => o.OrderDate) };
        return sorted.ThenBy(o => o.Id).Select(o => new PurchaseSummary(o.Id, o.PurchaseOrderNumber, o.SupplierId, o.Supplier.Name, o.Status, o.OrderDate,
            o.ExpectedDeliveryDate, o.TotalAmount, o.Items.Sum(i => (long)i.QuantityOrdered), o.Items.Sum(i => (long)i.QuantityReceived), o.Items.Sum(i => (long)i.QuantityOrdered - i.QuantityReceived)));
    }
    public Task<ReportPage<PurchaseSummary>> List(PurchaseFilter filter, CancellationToken ct) => ReportingService.Page(Rows(db, filter), new() { Page = filter.Page, PageSize = filter.PageSize }, ct);
    public Task<PurchaseResponse?> Get(Guid id, CancellationToken ct) => db.PurchaseOrders.AsNoTracking().Where(o => o.Id == id)
        .Select(o => new PurchaseResponse(o.Id, o.PurchaseOrderNumber, o.SupplierId, o.Supplier.Name, o.Status, o.OrderDate, o.ExpectedDeliveryDate, o.TotalAmount,
            o.Notes, o.CreatedByUserId, o.CreatedByUser.Name, o.CreatedAt, o.UpdatedAt, o.Items.OrderBy(i => i.Product.Name).ThenBy(i => i.Id)
                .Select(i => new PurchaseItemResponse(i.Id, i.ProductId, i.Product.Name, i.QuantityOrdered, i.QuantityReceived, i.QuantityOrdered - i.QuantityReceived, i.UnitCost, i.LineTotal)).ToList())).SingleOrDefaultAsync(ct);
    private Task<PurchaseOrder?> Lock(Guid id, CancellationToken ct) => db.PurchaseOrders.FromSqlInterpolated($"SELECT * FROM \"PurchaseOrders\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    private async Task ValidateReferences(Guid supplierId, IEnumerable<Guid> products, CancellationToken ct)
    {
        // Shared reference locks coordinate deactivation with draft creation/submission.
        var supplier = await db.Suppliers.FromSqlInterpolated($"SELECT * FROM \"Suppliers\" WHERE \"Id\" = {supplierId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (supplier is null || !supplier.IsActive) throw new BusinessException("supplierUnavailable");
        foreach (var id in products.Distinct().Order())
        {
            var product = await db.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (product is null) throw new BusinessException("productUnavailable");
            if (!product.IsActive) throw new BusinessException("productInactive");
        }
    }
    public async Task<PurchaseResponse?> Save(Guid? id, PurchaseRequest input, Guid user, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var order = id is null ? new PurchaseOrder { CreatedByUserId = user } : await Lock(id.Value, ct);
        if (order is null) return null;
        if (order.Status != PurchaseOrderStatus.Draft) throw new BusinessException("purchaseNotDraft", 409);
        await ValidateReferences(input.SupplierId, input.Items.Select(i => i.ProductId), ct);
        if (id is not null) await db.Entry(order).Collection(o => o.Items).LoadAsync(ct);
        else
        {
            var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"PurchaseOrderNumbers\"') AS \"Value\"").SingleAsync(ct);
            order.PurchaseOrderNumber = $"PO-{DateTime.UtcNow:yyyyMMdd}-{number:D6}"; db.PurchaseOrders.Add(order);
        }
        order.SupplierId = input.SupplierId; order.ExpectedDeliveryDate = input.ExpectedDeliveryDate?.ToUniversalTime(); order.Notes = input.Notes.Trim();
        foreach (var removed in order.Items.Where(i => !input.Items.Any(x => x.ProductId == i.ProductId)).ToArray()) { db.PurchaseOrderItems.Remove(removed); order.Items.Remove(removed); }
        foreach (var line in input.Items)
        {
            var item = order.Items.SingleOrDefault(i => i.ProductId == line.ProductId);
            if (item is null) { item = new() { ProductId = line.ProductId, PurchaseOrderId = order.Id }; order.Items.Add(item); db.PurchaseOrderItems.Add(item); }
            item.QuantityOrdered = line.QuantityOrdered; item.UnitCost = line.UnitCost; item.LineTotal = line.QuantityOrdered * line.UnitCost;
        }
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);
        if (order.TotalAmount > 9999999999999999.99m) throw new BusinessException("totalTooLarge");
        if (id is not null) order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct); return (await Get(order.Id, ct))!;
    }
    public async Task<PurchaseResponse?> Transition(Guid id, bool submit, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); var order = await Lock(id, ct);
        if (order is null) return null;
        await db.Entry(order).Collection(o => o.Items).LoadAsync(ct);
        if (submit)
        {
            if (order.Status != PurchaseOrderStatus.Draft || order.Items.Count == 0 || order.Items.Any(i => i.QuantityOrdered <= 0)) throw new BusinessException("invalidPurchaseTransition", 409);
            await ValidateReferences(order.SupplierId, order.Items.Select(i => i.ProductId), ct);
            order.Status = PurchaseOrderStatus.Submitted;
        }
        else
        {
            if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted) || order.Items.Any(i => i.QuantityReceived > 0)) throw new BusinessException("purchaseCannotCancel", 409);
            order.Status = PurchaseOrderStatus.Cancelled;
        }
        order.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Get(id, ct);
    }
    public async Task<ReportPage<ReceiptSummary>> Receipts(Guid orderId, int page, int pageSize, CancellationToken ct)
    {
        new PurchaseFilter { Page = page, PageSize = pageSize }.Validate();
        if (!await db.PurchaseOrders.AnyAsync(o => o.Id == orderId, ct)) throw new BusinessException("purchaseNotFound", 404);
        return await ReportingService.Page(db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId == orderId).OrderByDescending(r => r.ReceivedAt).ThenBy(r => r.Id)
            .Select(r => new ReceiptSummary(r.Id, r.ReceiptNumber, r.ReceivedAt, r.CreatedByUser.Name, r.Items.Count, r.Items.Sum(i => (long)i.QuantityReceived))), new() { Page = page, PageSize = pageSize }, ct);
    }
    public Task<ReceiptResponse?> Receipt(Guid id, CancellationToken ct) => db.GoodsReceipts.AsNoTracking().Where(r => r.Id == id)
        .Select(r => new ReceiptResponse(r.Id, r.ReceiptNumber, r.PurchaseOrderId, r.PurchaseOrder.PurchaseOrderNumber, r.PurchaseOrder.SupplierId, r.PurchaseOrder.Supplier.Name,
            r.ReceivedAt, r.Notes, r.CreatedByUserId, r.CreatedByUser.Name, r.Items.OrderBy(i => i.Id)
                .Select(i => new ReceiptItemResponse(i.Id, i.PurchaseOrderItemId, i.PurchaseOrderItem.ProductId, i.PurchaseOrderItem.Product.Name, i.QuantityReceived)).ToList(), r.WarehouseId, r.Warehouse.Name)).SingleOrDefaultAsync(ct);
    private static string Fingerprint(string canonical, Guid warehouse) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v2:{warehouse:N}:{canonical}")));
    public async Task<(ReceiptResponse Receipt, bool Replay)> Receive(Guid id, ReceiptRequest input, Guid user, Guid key, CancellationToken ct)
    {
        var canonical = JsonSerializer.Serialize(new { purchaseOrderId = id, notes = input.Notes.Trim(), items = input.Items.OrderBy(i => i.PurchaseOrderItemId).ToArray() });
        var legacyFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var lockId = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"receipt:{user:N}:{key:N}")));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Cross-process transaction-scoped advisory lock precedes any order/inventory locks.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        var previous = await db.GoodsReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.CreatedByUserId == user && r.IdempotencyKey == key, ct);
        if (previous is not null)
        {
            var selectedWarehouse = input.WarehouseId ?? previous.WarehouseId;
            var replayFingerprint = previous.FingerprintVersion == 1 ? legacyFingerprint : Fingerprint(canonical, selectedWarehouse);
            if (selectedWarehouse != previous.WarehouseId || previous.PayloadFingerprint != replayFingerprint) throw new BusinessException("receiptKeyConflict", 409);
            return ((await Receipt(previous.Id, ct))!, true);
        }
        var warehouse = await InventoryService.ResolveWarehouse(db, input.WarehouseId, ct);
        var fingerprint = Fingerprint(canonical, warehouse);
        var order = await Lock(id, ct) ?? throw new BusinessException("purchaseNotFound", 404);
        if (order.Status is not (PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived)) throw new BusinessException("purchaseNotReceivable", 409);
        await db.Entry(order).Collection(o => o.Items).LoadAsync(ct);
        var selected = new List<(PurchaseOrderItem Item, int Quantity)>();
        foreach (var line in input.Items)
        {
            var item = order.Items.SingleOrDefault(i => i.Id == line.PurchaseOrderItemId) ?? throw new BusinessException("receiptItemMismatch");
            if (line.QuantityReceived > item.QuantityOrdered - item.QuantityReceived) throw new BusinessException("receiptExceedsRemaining", 409);
            selected.Add((item, line.QuantityReceived));
        }
        var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"GoodsReceiptNumbers\"') AS \"Value\"").SingleAsync(ct);
        var receipt = new GoodsReceipt { WarehouseId = warehouse, PurchaseOrderId = id, CreatedByUserId = user, IdempotencyKey = key, PayloadFingerprint = fingerprint, Notes = input.Notes.Trim(), ReceiptNumber = $"GR-{DateTime.UtcNow:yyyyMMdd}-{number:D6}" };
        db.GoodsReceipts.Add(receipt);
        await InventoryService.LockWarehouses(db, [warehouse], ct);
        await InventoryService.LockProducts(db, selected.Select(i => i.Item.ProductId), ct);
        foreach (var (item, quantity) in selected.OrderBy(i => i.Item.ProductId))
        {
            var inventory = await InventoryService.LockBalance(db, warehouse, item.ProductId, ct);
            InventoryService.Apply(db, inventory, quantity, MovementType.PurchaseReceipt, user, "Purchase receipt", receiptId: receipt.Id);
            item.QuantityReceived += quantity;
            receipt.Items.Add(new GoodsReceiptItem { PurchaseOrderItemId = item.Id, QuantityReceived = quantity });
        }
        order.Status = order.Items.All(i => i.QuantityReceived == i.QuantityOrdered) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ((await Receipt(receipt.Id, ct))!, false);
    }
}
