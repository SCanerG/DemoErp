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
        var product = await db.Products.FindAsync([id], ct);
        if (product is null) return null;
        // An existing assignment may remain on an inactive category; changing it requires an active category.
        if (request.CategoryId != product.CategoryId) await ValidateCategory(request.CategoryId, ct);
        Apply(product, request); product.UpdatedAt = UtcNow();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return null; }
        return await Get(id, ct);
    }
    public async Task<bool> Delete(Guid id, CancellationToken ct)
    {
        if (await db.OrderItems.AnyAsync(i => i.ProductId == id, ct)) throw new BusinessException("productReferenced", 409);
        return await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct) > 0;
    }
    private static void Apply(Product p, ProductRequest r) { p.Name = r.Name.Trim(); p.Description = r.Description.Trim(); p.Price = r.Price; p.IsActive = r.IsActive; p.CategoryId = r.CategoryId; }
}
