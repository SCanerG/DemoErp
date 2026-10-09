using System.Data;
using System.Linq.Expressions;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Services;
public sealed class CategoryService(AppDbContext db)
{
    private static readonly Expression<Func<Category, CategoryResponse>> Projection = c => new(c.Id, c.Name, c.Description, c.IsActive, c.CreatedAt, c.UpdatedAt, c.Products.Count);
    public Task<List<CategoryResponse>> List(CancellationToken ct) => db.Categories.AsNoTracking().OrderBy(c => c.Name).Select(Projection).ToListAsync(ct);
    public Task<CategoryResponse?> Get(Guid id, CancellationToken ct) => db.Categories.AsNoTracking().Where(c => c.Id == id).Select(Projection).SingleOrDefaultAsync(ct);
    public async Task<CategoryResponse?> Save(Guid? id, CategoryRequest request, CancellationToken ct)
    {
        var c = id is null ? new Category() : await db.Categories.FindAsync([id.Value], ct);
        if (c is null) return null;
        c.Name = request.Name.Trim(); c.Description = request.Description.Trim(); c.IsActive = request.IsActive;
        if (id is null) db.Categories.Add(c); else c.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return null; }
        return await Get(c.Id, ct);
    }
    public async Task<bool> Delete(Guid id, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct)) throw new BusinessException("categoryReferenced", 409);
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null) return false;
        db.Categories.Remove(category); await db.SaveChangesAsync(ct); return true;
    }
}
public sealed class CustomerService(AppDbContext db)
{
    private static readonly Expression<Func<Customer, CustomerResponse>> Projection = c => new(c.Id, c.Name, c.Email, c.Phone, c.Address, c.IsActive, c.CreatedAt, c.UpdatedAt, c.Orders.Count);
    public Task<List<CustomerResponse>> List(CancellationToken ct) => db.Customers.AsNoTracking().OrderBy(c => c.Name).Select(Projection).ToListAsync(ct);
    public Task<CustomerResponse?> Get(Guid id, CancellationToken ct) => db.Customers.AsNoTracking().Where(c => c.Id == id).Select(Projection).SingleOrDefaultAsync(ct);
    public async Task<CustomerResponse?> Save(Guid? id, CustomerRequest request, CancellationToken ct)
    {
        var c = id is null ? new Customer() : await db.Customers.FindAsync([id.Value], ct);
        if (c is null) return null;
        c.Name = request.Name.Trim(); c.Email = request.Email.Trim().ToLowerInvariant(); c.Phone = request.Phone.Trim(); c.Address = request.Address.Trim(); c.IsActive = request.IsActive;
        if (id is null) db.Customers.Add(c); else c.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return null; }
        return await Get(c.Id, ct);
    }
    public async Task<bool> Delete(Guid id, CancellationToken ct)
    {
        if (await db.Orders.AnyAsync(o => o.CustomerId == id, ct)) throw new BusinessException("customerReferenced", 409);
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return false;
        db.Customers.Remove(customer); await db.SaveChangesAsync(ct); return true;
    }
}
public sealed class OrderService(AppDbContext db)
{
    public Task<List<OrderSummary>> List(CancellationToken ct) => db.Orders.AsNoTracking().OrderByDescending(o => o.OrderDate)
        .Select(o => new OrderSummary(o.Id, o.OrderNumber, o.CustomerId, o.Customer.Name, o.Status, o.OrderDate, o.TotalAmount, o.Items.Count)).ToListAsync(ct);
    public Task<OrderResponse?> Get(Guid id, CancellationToken ct) => db.Orders.AsNoTracking().Where(o => o.Id == id)
        .Select(o => new OrderResponse(o.Id, o.OrderNumber, o.CustomerId, o.Customer.Name, o.Status, o.OrderDate, o.TotalAmount, o.CreatedAt, o.UpdatedAt,
            o.Items.OrderBy(i => i.Id).Select(i => new OrderItemResponse(i.Id, i.ProductId, i.Product.Name, i.Quantity, i.UnitPrice, i.LineTotal)).ToList(),
            db.InventoryMovements.Any(m => m.ReferenceId == o.Id && m.MovementType == MovementType.OrderDeduction), o.CompletedAt)).SingleOrDefaultAsync(ct);
    public async Task<OrderResponse> Create(OrderRequest request, CancellationToken ct)
    {
        // One snapshot for customer/product checks; all writes commit together. Disposal rolls back on any failure.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await db.Customers.AnyAsync(c => c.Id == request.CustomerId && c.IsActive, ct)) throw new BusinessException("customerUnavailable");
        var ids = request.Items.Select(i => i.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Price, p.IsActive }).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Length) throw new BusinessException("productUnavailable");
        if (products.Values.Any(p => !p.IsActive)) throw new BusinessException("productInactive");
        var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"OrderNumbers\"') AS \"Value\"").SingleAsync(ct);
        var order = new Order { CustomerId = request.CustomerId, OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{number:D6}" };
        foreach (var line in request.Items)
        {
            var price = products[line.ProductId].Price;
            order.Items.Add(new OrderItem { ProductId = line.ProductId, Quantity = line.Quantity, UnitPrice = price, LineTotal = price * line.Quantity });
        }
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);
        if (order.TotalAmount > 9999999999999999.99m) throw new BusinessException("totalTooLarge");
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await Get(order.Id, ct))!;
    }
    public async Task<OrderResponse?> ChangeStatus(Guid id, OrderStatus status, Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (order is null) return null;
        if (order.Status == status) return await Get(id, ct);
        var allowed = order.Status switch
        {
            OrderStatus.Pending => status is OrderStatus.Confirmed or OrderStatus.Cancelled,
            OrderStatus.Confirmed => status is OrderStatus.Completed or OrderStatus.Cancelled,
            _ => false
        };
        if (!allowed) throw new BusinessException("invalidTransition", 409);
        if (order.Status == OrderStatus.Pending && status == OrderStatus.Confirmed)
        {
            var lines = await db.OrderItems.Where(i => i.OrderId == id).GroupBy(i => i.ProductId)
                .Select(group => new { ProductId = group.Key, Quantity = group.Sum(i => (long)i.Quantity) }).ToListAsync(ct);
            if (lines.Count == 0) throw new BusinessException("invalidTransition", 409);
            foreach (var line in lines.OrderBy(i => i.ProductId))
            {
                var inventory = await InventoryService.Lock(db, line.ProductId, ct) ?? throw new BusinessException("inventoryNotFound", 404);
                InventoryService.Apply(db, inventory, line.Quantity, MovementType.OrderDeduction, userId, "Order confirmation", id);
            }
        }
        else if (order.Status == OrderStatus.Confirmed && status == OrderStatus.Cancelled)
        {
            // Return ONLY actual deductions. Pre-inventory confirmed orders have no
            // deductions to reverse; cancellation must not create fictitious stock.
            var deductions = await db.InventoryMovements.Where(m => m.ReferenceId == id && m.MovementType == MovementType.OrderDeduction)
                .Select(m => new { m.ProductId, m.Quantity }).ToListAsync(ct);
            foreach (var line in deductions.OrderBy(i => i.ProductId))
            {
                var inventory = await InventoryService.Lock(db, line.ProductId, ct) ?? throw new BusinessException("inventoryNotFound", 404);
                InventoryService.Apply(db, inventory, line.Quantity, MovementType.OrderCancellationReturn, userId, "Order cancellation", id);
            }
        }
        order.Status = status;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        if (status == OrderStatus.Completed) order.CompletedAt = order.UpdatedAt;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await Get(id, ct);
    }
}
