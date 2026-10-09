using System.Linq.Expressions;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Services;
public sealed class ProductService(AppDbContext db)
{
    private static readonly Expression<Func<Product, ProductResponse>> Projection = p => new(p.Id, p.Name, p.Description, p.Price, p.IsActive, p.CreatedAt, p.UpdatedAt, p.CategoryId, p.Category.Name);
    private static DateTimeOffset UtcNow() { var ticks = DateTimeOffset.UtcNow.Ticks; return new(ticks - ticks % 10, TimeSpan.Zero); }
    public Task<List<ProductResponse>> List(CancellationToken ct) => db.Products.AsNoTracking().OrderByDescending(p => p.CreatedAt).Select(Projection).ToListAsync(ct);
    public Task<ProductResponse?> Get(Guid id, CancellationToken ct) => db.Products.AsNoTracking().Where(p => p.Id == id).Select(Projection).SingleOrDefaultAsync(ct);
    private async Task ValidateCategory(Guid id, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == id && c.IsActive, ct)) throw new BusinessException("categoryUnavailable");
    }
    public async Task<ProductResponse> Create(ProductRequest request, CancellationToken ct)
    {
        await ValidateCategory(request.CategoryId, ct);
        var product = new Product { CreatedAt = UtcNow() };
        Apply(product, request); db.Products.Add(product); await db.SaveChangesAsync(ct);
        return (await Get(product.Id, ct))!;
    }
    public async Task<ProductResponse?> Update(Guid id, ProductRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var product = await db.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (product is null) return null;
        // An existing assignment may remain on an inactive category; changing it requires an active category.
        if (request.CategoryId != product.CategoryId) await ValidateCategory(request.CategoryId, ct);
        Apply(product, request); product.UpdatedAt = UtcNow();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return null; }
        await transaction.CommitAsync(ct);
        return await Get(id, ct);
    }
    public async Task<bool> Delete(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inventory = await InventoryService.Lock(db, id, ct);
        if (await db.OrderItems.AnyAsync(i => i.ProductId == id, ct)) throw new BusinessException("productReferenced", 409);
        if (inventory?.QuantityOnHand > 0 || await db.InventoryMovements.AnyAsync(m => m.ProductId == id, ct)) throw new BusinessException("productHasInventoryHistory", 409);
        var product = await db.Products.FindAsync([id], ct);
        if (product is null) return false;
        // The database cascades inventory AFTER deleting its parent; client-side
        // dependent deletion would correctly be rejected by the inventory guard.
        if (inventory is not null) db.Entry(inventory).State = EntityState.Detached;
        product.Inventory = null!;
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
    private static void Apply(Product p, ProductRequest r) { p.Name = r.Name.Trim(); p.Description = r.Description.Trim(); p.Price = r.Price; p.IsActive = r.IsActive; p.CategoryId = r.CategoryId; }
}
