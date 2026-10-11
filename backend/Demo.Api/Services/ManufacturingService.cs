using System.Linq.Expressions;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.Services;
public sealed class ManufacturingService(AppDbContext db)
{
    private static readonly Expression<Func<BillOfMaterials, BomResponse>> BomProjection = b => new(b.Id, b.ProductId, b.Product.Name,
        b.Version, b.Name, b.BaseQuantity, b.IsActive, b.ActivatedAt, b.CreatedAt, b.UpdatedAt, b.CreatedByUserId, b.CreatedByUser.Name,
        b.Items.OrderBy(i => i.MaterialProduct.Name).ThenBy(i => i.MaterialProductId).Select(i => new BomLine(i.MaterialProductId, i.MaterialProduct.Name, i.QuantityRequired)).ToList());
    public Task<BomResponse?> Bom(Guid id, CancellationToken ct) => db.BillOfMaterials.AsNoTracking().Where(b => b.Id == id).Select(BomProjection).SingleOrDefaultAsync(ct);
    public async Task<ReportPage<BomResponse>> Boms(ManufacturingFilter f, CancellationToken ct)
    {
        f.Validate(); var q = db.BillOfMaterials.AsNoTracking();
        if (f.ProductId.HasValue) q = q.Where(b => b.ProductId == f.ProductId);
        if (!string.IsNullOrWhiteSpace(f.Search)) { var s = f.Search.Trim().ToLower(); q = q.Where(b => b.Name.ToLower().Contains(s) || b.Product.Name.ToLower().Contains(s)); }
        if (!string.IsNullOrWhiteSpace(f.Version)) q = q.Where(b => b.Version == f.Version.Trim());
        if (f.BomStatus == "Active") q = q.Where(b => b.IsActive);
        if (f.BomStatus == "Draft") q = q.Where(b => b.ActivatedAt == null);
        if (f.BomStatus == "Inactive") q = q.Where(b => !b.IsActive && b.ActivatedAt != null);
        var count = await q.CountAsync(ct);
        var sorted = f.Sort == "version" ? q.OrderBy(b => b.Version).ThenBy(b => b.Id) : f.Sort == "oldest" ? q.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id) : q.OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id);
        return new(await sorted.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).Select(BomProjection).ToListAsync(ct), f.Page, f.PageSize, count);
    }
    // Governance serializes BOM edits/activation/snapshot creation, before warehouse/product locks.
    private Task Governance(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7419202609)", ct);
    private async Task ValidateProducts(Guid finished, IEnumerable<Guid> materials, CancellationToken ct)
    {
        var ids = materials.Append(finished).Distinct().ToArray();
        await InventoryService.LockProducts(db, ids, ct, true);
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (products.Single(p => p.Id == finished).ProductType != ProductType.FinishedGood
            || products.Any(p => p.Id != finished && p.ProductType != ProductType.RawMaterial) || materials.Contains(finished)) throw new BusinessException("invalidManufacturingProducts", 409);
    }
    public async Task<BomResponse?> SaveBom(Guid? id, BomRequest r, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Governance(ct);
        var b = id is null ? new BillOfMaterials { CreatedByUserId = actor } : await db.BillOfMaterials.FromSqlInterpolated($"SELECT * FROM \"BillOfMaterials\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (b is null) return null;
        if (b.ActivatedAt is not null) throw new BusinessException("bomImmutable", 409);
        await ValidateProducts(r.ProductId, r.Items.Select(i => i.MaterialProductId), ct);
        var version = r.Version.Trim();
        if (await db.BillOfMaterials.AnyAsync(x => x.ProductId == r.ProductId && x.Version == version && x.Id != b.Id, ct)) throw new BusinessException("duplicateBomVersion", 409);
        if (id is null) db.BillOfMaterials.Add(b);
        else { await db.Entry(b).Collection(x => x.Items).LoadAsync(ct); db.BillOfMaterialsItems.RemoveRange(b.Items); b.Items.Clear(); await db.SaveChangesAsync(ct); b.UpdatedAt = DateTimeOffset.UtcNow; }
        b.ProductId = r.ProductId; b.Version = version; b.Name = r.Name.Trim(); b.BaseQuantity = r.BaseQuantity;
        foreach (var i in r.Items) { var line = new BillOfMaterialsItem { BillOfMaterialsId = b.Id, MaterialProductId = i.MaterialProductId, QuantityRequired = i.QuantityRequired }; b.Items.Add(line); db.BillOfMaterialsItems.Add(line); }
        // Force an allowlisted audit for line-only edits without logging the full recipe.
        if (id is not null) db.Entry(b).Property(x => x.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Bom(b.Id, ct);
    }
    public async Task<bool> DeleteBom(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Governance(ct);
        var b = await db.BillOfMaterials.FromSqlInterpolated($"SELECT * FROM \"BillOfMaterials\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (b is null) return false;
        if (b.ActivatedAt is not null || await db.ProductionOrders.AnyAsync(o => o.BillOfMaterialsId == id, ct)) throw new BusinessException("bomImmutable", 409);
        await db.Entry(b).Collection(x => x.Items).LoadAsync(ct); db.BillOfMaterialsItems.RemoveRange(b.Items);
        await db.SaveChangesAsync(ct); db.BillOfMaterials.Remove(b); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
    public async Task<BomResponse?> ActivateBom(Guid id, bool activate, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Governance(ct);
        var b = await db.BillOfMaterials.FromSqlInterpolated($"SELECT * FROM \"BillOfMaterials\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (b is null) return null;
        if (b.IsActive == activate) return await Bom(id, ct);
        if (activate)
        {
            if (b.ActivatedAt is not null) throw new BusinessException("bomImmutable", 409);
            await db.Entry(b).Collection(x => x.Items).LoadAsync(ct);
            if (b.Items.Count == 0) throw new BusinessException("bomEmpty", 409);
            await ValidateProducts(b.ProductId, b.Items.Select(i => i.MaterialProductId), ct);
            var previous = await db.BillOfMaterials.SingleOrDefaultAsync(x => x.ProductId == b.ProductId && x.IsActive, ct);
            if (previous is not null) { previous.IsActive = false; previous.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); }
            b.ActivatedAt = DateTimeOffset.UtcNow;
        }
        b.IsActive = activate; b.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Bom(id, ct);
    }
    internal static int Calculate(int required, int planned, int basis)
    {
        if (required <= 0 || planned <= 0 || basis <= 0) throw new BusinessException("invalidManufacturingQuantity");
        var numerator = checked((long)required * planned);
        if (numerator % basis != 0) throw new BusinessException("fractionalMaterialRequirement");
        var quantity = numerator / basis;
        if (quantity is <= 0 or > int.MaxValue) throw new BusinessException("manufacturingOverflow");
        return (int)quantity;
    }
    private IQueryable<MaterialAvailability> AvailabilityRows(Guid id) => db.ProductionOrderMaterials.AsNoTracking().Where(m => m.ProductionOrderId == id)
        .Select(m => new { m, Available = db.WarehouseInventories.Where(i => i.WarehouseId == m.ProductionOrder.SourceWarehouseId && i.ProductId == m.MaterialProductId).Select(i => (int?)i.QuantityOnHand).FirstOrDefault() ?? 0 })
        .OrderBy(x => x.m.MaterialProduct.Name).ThenBy(x => x.m.MaterialProductId)
        .Select(x => new MaterialAvailability(x.m.MaterialProductId, x.m.MaterialProduct.Name, x.m.QuantityRequired, x.m.QuantityConsumed,
            x.m.ProductionOrder.SourceWarehouseId, x.m.ProductionOrder.SourceWarehouse.Name, x.Available,
            Math.Max(x.m.QuantityRequired - x.m.QuantityConsumed - x.Available, 0),
            x.m.QuantityConsumed == x.m.QuantityRequired || x.Available >= x.m.QuantityRequired - x.m.QuantityConsumed ? "Available" : x.Available == 0 ? "OutOfStock" : "Insufficient"));
    public async Task<List<MaterialAvailability>> Availability(Guid id, CancellationToken ct)
    {
        if (!await db.ProductionOrders.AnyAsync(o => o.Id == id, ct)) throw new BusinessException("productionNotFound", 404);
        return await AvailabilityRows(id).ToListAsync(ct);
    }
    public async Task<List<MaterialAvailability>> Preview(ProductionRequest r, CancellationToken ct)
    {
        var b = await db.BillOfMaterials.AsNoTracking().Include(x => x.Items).ThenInclude(i => i.MaterialProduct).SingleOrDefaultAsync(x => x.Id == r.BillOfMaterialsId && x.ProductId == r.ProductId && x.IsActive, ct) ?? throw new BusinessException("activeBomRequired", 409);
        var w = await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.SourceWarehouseId && x.IsActive, ct) ?? throw new BusinessException("warehouseUnavailable", 409);
        var materialIds = b.Items.Select(i => i.MaterialProductId).ToArray();
        var stock = await db.WarehouseInventories.AsNoTracking().Where(i => i.WarehouseId == w.Id && materialIds.Contains(i.ProductId)).ToDictionaryAsync(i => i.ProductId, i => i.QuantityOnHand, ct);
        return b.Items.Select(i => { var required = Calculate(i.QuantityRequired, r.PlannedQuantity, b.BaseQuantity); var available = stock.GetValueOrDefault(i.MaterialProductId); return new MaterialAvailability(i.MaterialProductId, i.MaterialProduct.Name, required, 0, w.Id, w.Name, available, Math.Max(required - available, 0), available >= required ? "Available" : available == 0 ? "OutOfStock" : "Insufficient"); }).ToList();
    }
    private IQueryable<ProductionOrder> Filter(ManufacturingFilter f)
    {
        f.Validate(); var q = db.ProductionOrders.AsNoTracking();
        if (f.ProductionOrderId.HasValue) q = q.Where(o => o.Id == f.ProductionOrderId);
        if (f.ProductId.HasValue) q = q.Where(o => o.ProductId == f.ProductId);
        if (f.Status.HasValue) q = q.Where(o => o.Status == f.Status);
        if (f.SourceWarehouseId.HasValue) q = q.Where(o => o.SourceWarehouseId == f.SourceWarehouseId);
        if (f.DestinationWarehouseId.HasValue) q = q.Where(o => o.DestinationWarehouseId == f.DestinationWarehouseId);
        if (f.Start.HasValue) q = q.Where(o => o.CreatedAt >= f.Start && o.CreatedAt <= f.End);
        if (!string.IsNullOrWhiteSpace(f.Search)) { var s = f.Search.Trim().ToLower(); q = q.Where(o => o.ProductionOrderNumber.ToLower().Contains(s) || o.Product.Name.ToLower().Contains(s)); }
        return q;
    }
    private IQueryable<ProductionResponse> ProductionRows(IQueryable<ProductionOrder> query) => query.Select(o => new ProductionResponse(o.Id, o.ProductionOrderNumber, o.ProductId, o.Product.Name, o.BillOfMaterialsId, o.BillOfMaterials.Version, o.PlannedQuantity, o.ProducedQuantity,
        o.SourceWarehouseId, o.SourceWarehouse.Name, o.DestinationWarehouseId, o.DestinationWarehouse.Name, o.Status, o.PlannedStartDate, o.PlannedEndDate,
        o.StartedAt, o.CompletedAt, o.Notes, o.CreatedAt, o.UpdatedAt, o.CreatedByUserId, o.CreatedByUser.Name, o.StartedByUserId, o.StartedByUser == null ? null : o.StartedByUser.Name, o.CompletedByUserId, o.CompletedByUser == null ? null : o.CompletedByUser.Name,
        o.Materials.OrderBy(m => m.MaterialProduct.Name).ThenBy(m => m.MaterialProductId).Select(m => new MaterialAvailability(m.MaterialProductId, m.MaterialProduct.Name, m.QuantityRequired, m.QuantityConsumed,
            o.SourceWarehouseId, o.SourceWarehouse.Name,
            db.WarehouseInventories.Where(i => i.WarehouseId == o.SourceWarehouseId && i.ProductId == m.MaterialProductId).Select(i => (int?)i.QuantityOnHand).FirstOrDefault() ?? 0,
            Math.Max(m.QuantityRequired - m.QuantityConsumed - (db.WarehouseInventories.Where(i => i.WarehouseId == o.SourceWarehouseId && i.ProductId == m.MaterialProductId).Select(i => (int?)i.QuantityOnHand).FirstOrDefault() ?? 0), 0),
            m.QuantityRequired - m.QuantityConsumed <= (db.WarehouseInventories.Where(i => i.WarehouseId == o.SourceWarehouseId && i.ProductId == m.MaterialProductId).Select(i => (int?)i.QuantityOnHand).FirstOrDefault() ?? 0) ? "Available" :
                (db.WarehouseInventories.Where(i => i.WarehouseId == o.SourceWarehouseId && i.ProductId == m.MaterialProductId).Select(i => (int?)i.QuantityOnHand).FirstOrDefault() ?? 0) == 0 ? "OutOfStock" : "Insufficient")).ToList()));
    public Task<ProductionResponse?> Production(Guid id, CancellationToken ct) => ProductionRows(db.ProductionOrders.AsNoTracking().Where(o => o.Id == id)).SingleOrDefaultAsync(ct);
    public async Task<ReportPage<ProductionResponse>> Productions(ManufacturingFilter f, CancellationToken ct)
    {
        var q = Filter(f); var count = await q.CountAsync(ct);
        var sorted = f.Sort == "oldest" ? q.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id) : q.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id);
        return new(await ProductionRows(sorted.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)).ToListAsync(ct), f.Page, f.PageSize, count);
    }
    public async Task<ProductionResponse?> SaveProduction(Guid? id, ProductionRequest r, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Governance(ct);
        var o = id is null ? new ProductionOrder { CreatedByUserId = actor } : await db.ProductionOrders.FromSqlInterpolated($"SELECT * FROM \"ProductionOrders\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (o is null) return null;
        if (o.Status != ProductionStatus.Draft) throw new BusinessException("productionNotDraft", 409);
        var b = await db.BillOfMaterials.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == r.BillOfMaterialsId && x.ProductId == r.ProductId && x.IsActive, ct) ?? throw new BusinessException("activeBomRequired", 409);
        await InventoryService.LockWarehouses(db, [r.SourceWarehouseId, r.DestinationWarehouseId], ct);
        await ValidateProducts(r.ProductId, b.Items.Select(i => i.MaterialProductId), ct);
        if (b.Items.Count == 0) throw new BusinessException("bomEmpty", 409);
        if (id is null) { var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"ProductionNumbers\"') AS \"Value\"").SingleAsync(ct); o.ProductionOrderNumber = $"MFG-{DateTime.UtcNow:yyyyMMdd}-{number:D6}"; db.ProductionOrders.Add(o); }
        else { await db.Entry(o).Collection(x => x.Materials).LoadAsync(ct); db.ProductionOrderMaterials.RemoveRange(o.Materials); o.Materials.Clear(); await db.SaveChangesAsync(ct); o.UpdatedAt = DateTimeOffset.UtcNow; }
        o.ProductId = r.ProductId; o.BillOfMaterialsId = b.Id; o.PlannedQuantity = r.PlannedQuantity;
        o.SourceWarehouseId = r.SourceWarehouseId; o.DestinationWarehouseId = r.DestinationWarehouseId;
        o.PlannedStartDate = r.PlannedStartDate?.ToUniversalTime(); o.PlannedEndDate = r.PlannedEndDate?.ToUniversalTime(); o.Notes = r.Notes.Trim();
        foreach (var i in b.Items) { var m = new ProductionOrderMaterial { ProductionOrderId = o.Id, MaterialProductId = i.MaterialProductId, QuantityRequired = Calculate(i.QuantityRequired, r.PlannedQuantity, b.BaseQuantity) }; o.Materials.Add(m); db.ProductionOrderMaterials.Add(m); }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Production(o.Id, ct);
    }
    public async Task<ProductionResponse?> Transition(Guid id, string command, Guid actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var o = await db.ProductionOrders.FromSqlInterpolated($"SELECT * FROM \"ProductionOrders\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (o is null) return null;
        // Network retries return the original result; no second movement or audit.
        if (command == "start" && o.Status is ProductionStatus.InProgress or ProductionStatus.Completed || command == "complete" && o.Status == ProductionStatus.Completed
            || command == "release" && o.Status == ProductionStatus.Released || command == "cancel" && o.Status == ProductionStatus.Cancelled) return await Production(id, ct);
        var next = (command, o.Status) switch {
            ("release", ProductionStatus.Draft) => ProductionStatus.Released,
            ("start", ProductionStatus.Released) => ProductionStatus.InProgress,
            ("complete", ProductionStatus.InProgress) => ProductionStatus.Completed,
            ("cancel", ProductionStatus.Draft or ProductionStatus.Released) => ProductionStatus.Cancelled,
            _ => throw new BusinessException("invalidProductionTransition", 409)
        };
        await db.Entry(o).Collection(x => x.Materials).LoadAsync(ct);
        if (command is "release" or "start" or "complete")
        {
            if (o.Materials.Count == 0 || o.Materials.Any(m => m.QuantityRequired <= 0)) throw new BusinessException("invalidMaterialSnapshot", 409);
            await InventoryService.LockWarehouses(db, command == "complete" ? [o.DestinationWarehouseId] : [o.SourceWarehouseId, o.DestinationWarehouseId], ct);
            await InventoryService.LockProducts(db, o.Materials.Select(m => m.MaterialProductId).Append(o.ProductId), ct, true);
        }
        if (command == "start")
        {
            var balances = new Dictionary<Guid, WarehouseInventory>();
            foreach (var m in o.Materials.OrderBy(m => m.MaterialProductId)) { var balance = await InventoryService.LockBalance(db, o.SourceWarehouseId, m.MaterialProductId, ct); balances.Add(m.MaterialProductId, balance); if (m.QuantityConsumed != 0 || balance.QuantityOnHand < m.QuantityRequired) throw new BusinessException("insufficientStock", 409); }
            foreach (var m in o.Materials) { InventoryService.Apply(db, balances[m.MaterialProductId], m.QuantityRequired, MovementType.ProductionConsumption, actor, o.ProductionOrderNumber, productionId: id); m.QuantityConsumed = m.QuantityRequired; }
            o.StartedAt = DateTimeOffset.UtcNow; o.StartedByUserId = actor;
        }
        if (command == "complete")
        {
            var movements = await db.InventoryMovements.AsNoTracking().Where(m => m.ProductionOrderId == id && m.MovementType == MovementType.ProductionConsumption).ToListAsync(ct);
            if (movements.Count != o.Materials.Count || o.Materials.Any(m => m.QuantityConsumed != m.QuantityRequired || !movements.Any(x => x.ProductId == m.MaterialProductId && x.Quantity == m.QuantityRequired && x.WarehouseId == o.SourceWarehouseId))) throw new BusinessException("invalidMaterialSnapshot", 409);
            var balance = await InventoryService.LockBalance(db, o.DestinationWarehouseId, o.ProductId, ct);
            InventoryService.Apply(db, balance, o.PlannedQuantity, MovementType.ProductionOutput, actor, o.ProductionOrderNumber, productionId: id);
            o.ProducedQuantity = o.PlannedQuantity; o.CompletedAt = DateTimeOffset.UtcNow; o.CompletedByUserId = actor;
        }
        o.Status = next; o.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Production(id, ct);
    }
    public async Task<ManufacturingKpis> Kpis(CancellationToken ct) => new(await db.ProductionOrders.CountAsync(o => o.Status == ProductionStatus.Draft, ct),
        await db.ProductionOrders.CountAsync(o => o.Status == ProductionStatus.Released, ct), await db.ProductionOrders.CountAsync(o => o.Status == ProductionStatus.InProgress, ct),
        await db.ProductionOrders.CountAsync(o => o.Status == ProductionStatus.Completed, ct), await db.ProductionOrders.SumAsync(o => (long)o.ProducedQuantity, ct));
    public async Task<ReportPage<MaterialRequirementRow>> Requirements(ManufacturingFilter f, bool shortagesOnly, CancellationToken ct)
    {
        var orders = Filter(f).Where(o => o.Status == ProductionStatus.Draft || o.Status == ProductionStatus.Released);
        var rows = db.ProductionOrderMaterials.AsNoTracking().Where(m => orders.Any(o => o.Id == m.ProductionOrderId))
            .Select(m => new { m, Available = db.WarehouseInventories.Where(i => i.WarehouseId == m.ProductionOrder.SourceWarehouseId && i.ProductId == m.MaterialProductId).Select(i => (int?)i.QuantityOnHand).FirstOrDefault() ?? 0 });
        if (shortagesOnly) rows = rows.Where(x => x.m.QuantityRequired > x.Available);
        var count = await rows.CountAsync(ct);
        var result = await rows.OrderBy(x => x.m.ProductionOrder.ProductionOrderNumber).ThenBy(x => x.m.MaterialProductId).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)
            .Select(x => new MaterialRequirementRow(x.m.ProductionOrderId, x.m.ProductionOrder.ProductionOrderNumber, x.m.ProductionOrder.Product.Name, x.m.MaterialProductId,
                x.m.MaterialProduct.Name, x.m.QuantityRequired, x.Available, Math.Max(x.m.QuantityRequired - x.Available, 0), x.m.ProductionOrder.SourceWarehouseId, x.m.ProductionOrder.SourceWarehouse.Name)).ToListAsync(ct);
        return new(result, f.Page, f.PageSize, count);
    }
}
