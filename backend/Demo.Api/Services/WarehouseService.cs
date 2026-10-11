using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.Services;

public sealed class WarehouseService(AppDbContext db)
{
    private IQueryable<WarehouseResponse> Rows() => db.Warehouses.AsNoTracking().Select(w => new WarehouseResponse(w.Id, w.Code, w.Name, w.Description, w.Location,
        w.IsActive, w.IsDefault, w.CreatedAt, w.UpdatedAt, db.WarehouseInventories.Count(i => i.WarehouseId == w.Id && i.QuantityOnHand > 0),
        db.WarehouseInventories.Where(i => i.WarehouseId == w.Id).Sum(i => (long)i.QuantityOnHand),
        db.WarehouseInventories.Count(i => i.WarehouseId == w.Id && i.QuantityOnHand > 0 && i.QuantityOnHand <= i.MinimumStockLevel),
        db.Products.Count() - db.WarehouseInventories.Count(i => i.WarehouseId == w.Id && i.QuantityOnHand > 0)) { Id=w.Id, Name=w.Name, Code=w.Code, IsActive=w.IsActive, IsDefault=w.IsDefault });
    public Task<WarehouseResponse?> Get(Guid id, CancellationToken ct) => Rows().SingleOrDefaultAsync(w => w.Id == id, ct);
    public Task<ReportPage<WarehouseResponse>> List(WarehouseFilter f, CancellationToken ct)
    {
        f.Validate(); var q = Rows(); if (f.IsDefault.HasValue) q = q.Where(w => w.IsDefault == f.IsDefault); if (f.IsActive.HasValue) q = q.Where(w => w.IsActive == f.IsActive);
        if (!string.IsNullOrWhiteSpace(f.Search)) { var search = f.Search.Trim().ToLower(); q = q.Where(w => w.Name.ToLower().Contains(search) || w.Code.ToLower().Contains(search)); }
        return ReportingService.Page(q.OrderBy(w => w.Name).ThenBy(w => w.Id), new() { Page = f.Page, PageSize = f.PageSize }, ct);
    }
    private Task GovernanceLock(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7419202608)", ct);
    private Task<Warehouse?> Lock(Guid id, CancellationToken ct) => db.Warehouses.FromSqlInterpolated($"SELECT * FROM \"Warehouses\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    public async Task<WarehouseResponse?> Save(Guid? id, WarehouseRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await GovernanceLock(ct);
        var w = id is null ? new Warehouse() : await Lock(id.Value, ct); if (w is null) return null;
        var code = request.Code.Trim().ToUpperInvariant(); if (await db.Warehouses.AnyAsync(x => x.Code == code && x.Id != w.Id, ct)) throw new BusinessException("duplicateWarehouseCode", 409);
        w.Code = code; w.Name = request.Name.Trim(); w.Description = request.Description.Trim(); w.Location = request.Location.Trim();
        if (id is null) db.Warehouses.Add(w); else w.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Get(w.Id, ct);
    }
    public async Task<WarehouseResponse?> Status(Guid id, bool active, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await GovernanceLock(ct); var w = await Lock(id, ct); if (w is null) return null;
        if (!active && w.IsActive)
        {
            if (w.IsDefault) throw new BusinessException("defaultWarehouseRequired", 409);
            if (await db.WarehouseInventories.AnyAsync(i => i.WarehouseId == id && i.QuantityOnHand > 0, ct)) throw new BusinessException("warehouseHasStock", 409);
            if (await db.Orders.AnyAsync(o => o.WarehouseId == id && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed), ct)
                || await db.ProductionOrders.AnyAsync(o => (o.SourceWarehouseId == id || o.DestinationWarehouseId == id) && (o.Status == ProductionStatus.Draft || o.Status == ProductionStatus.Released || o.Status == ProductionStatus.InProgress), ct)
                || await db.StockTransfers.AnyAsync(t => t.Status == TransferStatus.Draft && (t.SourceWarehouseId == id || t.DestinationWarehouseId == id), ct)) throw new BusinessException("warehouseHasOperations", 409);
        }
        if (w.IsActive != active) { w.IsActive = active; w.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct); return await Get(id, ct);
    }
    public async Task<WarehouseResponse?> SetDefault(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await GovernanceLock(ct);
        var previousId = await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).SingleAsync(ct);
        // Sorted row locks avoid opposing switch requests; clearing first satisfies the partial unique index.
        Warehouse? selected = null; Warehouse? previous = null;
        foreach (var key in new[] { id, previousId }.Distinct().Order()) { var w = await Lock(key, ct); if (key == id) selected = w; if (key == previousId) previous = w; }
        if (selected is null) return null; if (!selected.IsActive) throw new BusinessException("warehouseUnavailable", 409);
        if (id != previousId)
        {
            previous!.IsDefault = false; previous.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            selected.IsDefault = true; selected.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return await Get(id, ct);
    }
    internal IQueryable<WarehouseStock> StockRows(Guid id)
    {
        return from p in db.Products.AsNoTracking()
            join i in db.WarehouseInventories.AsNoTracking().Where(i => i.WarehouseId == id) on p.Id equals i.ProductId into balances
            from i in balances.DefaultIfEmpty()
            select new WarehouseStock(i == null ? Guid.Empty : i.Id, id, db.Warehouses.Where(w => w.Id == id).Select(w => w.Name).First(), p.Id, p.Name, p.Category.Name, p.IsActive,
                i == null ? 0 : i.QuantityOnHand, i == null ? 0 : i.MinimumStockLevel,
                i == null || i.QuantityOnHand == 0 ? "Out" : i.QuantityOnHand <= i.MinimumStockLevel ? "Low" : "Healthy", i == null ? null : i.UpdatedAt) { ProductId=p.Id, ProductName=p.Name, StockStatus=i == null || i.QuantityOnHand == 0 ? "Out" : i.QuantityOnHand <= i.MinimumStockLevel ? "Low" : "Healthy" };
    }
    public async Task<ReportPage<WarehouseStock>> Stock(Guid id, WarehouseFilter f, CancellationToken ct)
    {
        f.Validate(); if (!await db.Warehouses.AnyAsync(w => w.Id == id, ct)) throw new BusinessException("warehouseNotFound", 404);
        var q = StockRows(id); if (f.CategoryId.HasValue) q = q.Where(i => db.Products.Any(p => p.Id == i.ProductId && p.CategoryId == f.CategoryId));
        if (!string.IsNullOrWhiteSpace(f.Search)) { var search = f.Search.Trim().ToLower(); q = q.Where(i => i.ProductName.ToLower().Contains(search)); }
        if (f.StockStatus is not null) q = q.Where(i => i.StockStatus == f.StockStatus);
        return await ReportingService.Page(q.OrderBy(i => i.ProductName).ThenBy(i => i.ProductId), new() { Page = f.Page, PageSize = f.PageSize }, ct);
    }
    public async Task<WarehouseStock?> ProductStock(Guid id, Guid product, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(w => w.Id == id, ct)) throw new BusinessException("warehouseNotFound", 404);
        return await StockRows(id).SingleOrDefaultAsync(i => i.ProductId == product, ct);
    }
    public async Task<ReportPage<WarehouseStock>> ProductWarehouses(Guid product, WarehouseFilter f, CancellationToken ct)
    {
        f.Validate(); if (!await db.Products.AnyAsync(p => p.Id == product, ct)) throw new BusinessException("productUnavailable", 404);
        var q = from w in db.Warehouses.AsNoTracking() join i in db.WarehouseInventories.Where(i => i.ProductId == product) on w.Id equals i.WarehouseId into balances
            from i in balances.DefaultIfEmpty() select new WarehouseStock(i == null ? Guid.Empty : i.Id, w.Id, w.Name, product, db.Products.Where(p => p.Id == product).Select(p => p.Name).First(),
                db.Products.Where(p => p.Id == product).Select(p => p.Category.Name).First(), w.IsActive, i == null ? 0 : i.QuantityOnHand, i == null ? 0 : i.MinimumStockLevel,
                i == null || i.QuantityOnHand == 0 ? "Out" : i.QuantityOnHand <= i.MinimumStockLevel ? "Low" : "Healthy", i == null ? null : i.UpdatedAt) { WarehouseId=w.Id, WarehouseName=w.Name, IsActive=w.IsActive, StockStatus=i == null || i.QuantityOnHand == 0 ? "Out" : i.QuantityOnHand <= i.MinimumStockLevel ? "Low" : "Healthy" };
        if (f.IsActive.HasValue) q = q.Where(i => i.IsActive == f.IsActive);
        if (f.StockStatus is not null) q = q.Where(i => i.StockStatus == f.StockStatus);
        if (!string.IsNullOrWhiteSpace(f.Search)) q = q.Where(i => i.WarehouseName.ToLower().Contains(f.Search.Trim().ToLower()));
        return await ReportingService.Page(q.OrderBy(i => i.WarehouseName).ThenBy(i => i.WarehouseId), new() { Page = f.Page, PageSize = f.PageSize }, ct);
    }
    public async Task<WarehouseReport> Report(WarehouseFilter f, CancellationToken ct)
    {
        var total = await db.Warehouses.CountAsync(ct); var products = await db.Products.CountAsync(ct); var active = await db.Warehouses.CountAsync(w => w.IsActive, ct);
        var units = await db.WarehouseInventories.SumAsync(i => (long)i.QuantityOnHand, ct);
        var low = await db.WarehouseInventories.CountAsync(i => i.QuantityOnHand > 0 && i.QuantityOnHand <= i.MinimumStockLevel, ct);
        var stocked = await db.WarehouseInventories.CountAsync(i => i.QuantityOnHand > 0, ct); var completed = await db.StockTransfers.CountAsync(t => t.Status == TransferStatus.Completed, ct);
        return new(total, active, units, low, (long)total * products - stocked, completed, await List(f, ct));
    }
}
