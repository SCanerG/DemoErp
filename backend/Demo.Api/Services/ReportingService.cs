using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Services;

public sealed class ReportingService(AppDbContext db)
{
    private IQueryable<Order> Completed(DateTimeOffset start, DateTimeOffset end) => db.Orders.AsNoTracking()
        .Where(o => o.Status == OrderStatus.Completed && o.CompletedAt >= start && o.CompletedAt < end);
    public async Task<DashboardSummary> Summary(ReportFilter filter, CancellationToken ct)
    {
        var (start, end) = filter.Validate("dashboard");
        var sales = await SalesMetrics(start, end, ct);
        var orders = await db.Orders.AsNoTracking().Where(o => o.OrderDate >= start && o.OrderDate < end)
            .GroupBy(o => 1).Select(g => new { Total = g.Count(), Pending = g.Count(o => o.Status == OrderStatus.Pending) }).SingleOrDefaultAsync(ct);
        // Each small aggregate executes sequentially: a scoped DbContext cannot run concurrent commands.
        var customers = await db.Customers.AsNoTracking().CountAsync(c => c.IsActive, ct);
        var products = await db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var stock = await db.Inventories.AsNoTracking().GroupBy(i => 1)
            .Select(g => new { Low = g.Count(i => i.QuantityOnHand > 0 && i.QuantityOnHand <= i.MinimumStockLevel), Out = g.Count(i => i.QuantityOnHand == 0) }).SingleOrDefaultAsync(ct);
        var legacy = await db.Orders.AsNoTracking().CountAsync(o => o.Status == OrderStatus.Completed && o.CompletedAt == null, ct);
        return new(sales.Sales, orders?.Total ?? 0, sales.CompletedOrders, orders?.Pending ?? 0, customers, products, stock?.Low ?? 0, stock?.Out ?? 0, legacy);
    }
    public async Task<SalesTrend> Trend(ReportFilter filter, CancellationToken ct)
    {
        var (start, end) = filter.Validate("dashboard");
        var monthly = end - start > TimeSpan.FromDays(90);
        var query = Completed(start, end);
        // UTC parts translate to PostgreSQL date_part with AT TIME ZONE 'UTC'.
        var values = await query.GroupBy(o => new { o.CompletedAt!.Value.UtcDateTime.Year, o.CompletedAt.Value.UtcDateTime.Month,
            Day = monthly ? 1 : o.CompletedAt.Value.UtcDateTime.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Sales = g.Sum(o => o.TotalAmount) }).ToListAsync(ct);
        var lookup = values.ToDictionary(x => new DateTime(x.Year, x.Month, x.Day), x => x.Sales);
        var cursor = monthly ? new DateTime(start.Year, start.Month, 1) : start.UtcDateTime.Date;
        var points = new List<TrendPoint>();
        // Only bounded zero filling happens in memory; sales aggregation stays in SQL.
        while (cursor < end.UtcDateTime)
        {
            points.Add(new(DateTime.SpecifyKind(cursor, DateTimeKind.Utc), lookup.GetValueOrDefault(cursor)));
            cursor = monthly ? cursor.AddMonths(1) : cursor.AddDays(1);
        }
        return new(monthly ? "month" : "day", points);
    }
    public async Task<List<StatusCount>> Statuses(ReportFilter filter, CancellationToken ct)
    {
        var (start, end) = filter.Validate("dashboard");
        var counts = await db.Orders.AsNoTracking().Where(o => o.OrderDate >= start && o.OrderDate < end)
            .GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        return Enum.GetValues<OrderStatus>().Select(s => new StatusCount(s, counts.SingleOrDefault(c => c.Status == s)?.Count ?? 0)).ToList();
    }
    public IQueryable<SalesRow> Sales(ReportFilter filter)
    {
        var (start, end) = filter.Validate("sales");
        var query = db.Orders.AsNoTracking().Where(o => o.OrderDate >= start && o.OrderDate < end);
        if (filter.CustomerId.HasValue) query = query.Where(o => o.CustomerId == filter.CustomerId);
        if (filter.Status.HasValue) query = query.Where(o => o.Status == filter.Status);
        if (filter.MinimumTotal.HasValue) query = query.Where(o => o.TotalAmount >= filter.MinimumTotal);
        if (filter.MaximumTotal.HasValue) query = query.Where(o => o.TotalAmount <= filter.MaximumTotal);
        var sorted = filter.Sort switch
        {
            "totalAmount" => filter.Descending ? query.OrderByDescending(o => o.TotalAmount) : query.OrderBy(o => o.TotalAmount),
            "customer" => filter.Descending ? query.OrderByDescending(o => o.Customer.Name) : query.OrderBy(o => o.Customer.Name),
            "orderNumber" => filter.Descending ? query.OrderByDescending(o => o.OrderNumber) : query.OrderBy(o => o.OrderNumber),
            "completedAt" => filter.Descending ? query.OrderByDescending(o => o.CompletedAt) : query.OrderBy(o => o.CompletedAt),
            _ => filter.Descending ? query.OrderByDescending(o => o.OrderDate) : query.OrderBy(o => o.OrderDate)
        };
        return sorted.ThenBy(o => o.Id).Select(o => new SalesRow(o.Id, o.OrderNumber, o.Customer.Name, o.OrderDate, o.CompletedAt, o.Status, o.Items.Count, o.TotalAmount));
    }
    public IQueryable<ProductSalesRow> Products(ReportFilter filter)
    {
        var (start, end) = filter.Validate("products");
        var lines = CompletedLines(start, end);
        if (filter.ProductId.HasValue) lines = lines.Where(i => i.ProductId == filter.ProductId);
        if (filter.CategoryId.HasValue) lines = lines.Where(i => i.Product.CategoryId == filter.CategoryId);
        var aggregates = lines.GroupBy(i => i.ProductId).Select(g => new
        { Id = g.Key, Quantity = g.Sum(i => (long)i.Quantity), Sales = g.Sum(i => i.LineTotal), Count = g.Select(i => i.OrderId).Distinct().Count() });
        var rows = from a in aggregates join p in db.Products.AsNoTracking() on a.Id equals p.Id
            select new { a.Id, p.Name, Category = p.Category.Name, a.Quantity, a.Sales, a.Count, Stock = p.Inventory!.QuantityOnHand };
        var sorted = filter.Sort switch
        {
            "product" => filter.Descending ? rows.OrderByDescending(x => x.Name) : rows.OrderBy(x => x.Name),
            "quantitySold" => filter.Descending ? rows.OrderByDescending(x => x.Quantity) : rows.OrderBy(x => x.Quantity),
            "currentStock" => filter.Descending ? rows.OrderByDescending(x => x.Stock) : rows.OrderBy(x => x.Stock),
            _ => filter.Descending ? rows.OrderByDescending(x => x.Sales) : rows.OrderBy(x => x.Sales)
        };
        return sorted.ThenBy(x => x.Id).Select(x => new ProductSalesRow(x.Id, x.Name, x.Category, x.Quantity, x.Sales, x.Count, x.Stock));
    }
    public IQueryable<CustomerSalesRow> Customers(ReportFilter filter, bool onlyWithSales = false)
    {
        var (start, end) = filter.Validate("customers");
        var completed = Completed(start, end).GroupBy(o => o.CustomerId).Select(g => new
        { Id = g.Key, Count = g.Count(), Sales = g.Sum(o => o.TotalAmount), Average = g.Average(o => o.TotalAmount), Last = g.Max(o => o.CompletedAt) });
        var created = db.Orders.AsNoTracking().Where(o => o.OrderDate >= start && o.OrderDate < end)
            .GroupBy(o => o.CustomerId).Select(g => new { Id = g.Key, Count = g.Count() });
        var customers = db.Customers.AsNoTracking();
        if (filter.CustomerId.HasValue) customers = customers.Where(c => c.Id == filter.CustomerId);
        if (!string.IsNullOrWhiteSpace(filter.Search)) customers = customers.Where(c => c.Name.Contains(filter.Search.Trim()));
        var rows = from c in customers
            join a in completed on c.Id equals a.Id into salesJoin from a in salesJoin.DefaultIfEmpty()
            join b in created on c.Id equals b.Id into createdJoin from b in createdJoin.DefaultIfEmpty()
            select new { c.Id, c.Name, Total = (int?)b.Count ?? 0, Count = (int?)a.Count ?? 0,
                Sales = (decimal?)a.Sales ?? 0, Average = (decimal?)a.Average ?? 0, Last = a.Last };
        if (onlyWithSales) rows = rows.Where(x => x.Count > 0);
        var sorted = filter.Sort switch
        {
            "customer" => filter.Descending ? rows.OrderByDescending(x => x.Name) : rows.OrderBy(x => x.Name),
            "totalOrders" => filter.Descending ? rows.OrderByDescending(x => x.Total) : rows.OrderBy(x => x.Total),
            "completedOrders" => filter.Descending ? rows.OrderByDescending(x => x.Count) : rows.OrderBy(x => x.Count),
            _ => filter.Descending ? rows.OrderByDescending(x => x.Sales) : rows.OrderBy(x => x.Sales)
        };
        return sorted.ThenBy(x => x.Id).Select(x => new CustomerSalesRow(x.Id, x.Name, x.Total, x.Count, x.Sales, x.Average, x.Last));
    }
    public IQueryable<InventoryReportRow> Inventory(ReportFilter filter)
    {
        filter.Validate("inventory");
        var query = db.Inventories.AsNoTracking();
        if (filter.CategoryId.HasValue) query = query.Where(i => i.Product.CategoryId == filter.CategoryId);
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(i => i.Product.Name.Contains(filter.Search.Trim()));
        query = filter.StockStatus switch
        {
            "Out" => query.Where(i => i.QuantityOnHand == 0),
            "Low" => query.Where(i => i.QuantityOnHand > 0 && i.QuantityOnHand <= i.MinimumStockLevel),
            "Healthy" => query.Where(i => i.QuantityOnHand > i.MinimumStockLevel), _ => query
        };
        var sorted = filter.Sort switch
        {
            "quantityOnHand" => filter.Descending ? query.OrderByDescending(i => i.QuantityOnHand) : query.OrderBy(i => i.QuantityOnHand),
            "minimumStockLevel" => filter.Descending ? query.OrderByDescending(i => i.MinimumStockLevel) : query.OrderBy(i => i.MinimumStockLevel),
            "updatedAt" => filter.Descending ? query.OrderByDescending(i => i.UpdatedAt) : query.OrderBy(i => i.UpdatedAt),
            _ => filter.Descending ? query.OrderByDescending(i => i.Product.Name) : query.OrderBy(i => i.Product.Name)
        };
        return sorted.ThenBy(i => i.ProductId).Select(i => new InventoryReportRow(i.ProductId, i.Product.Name, i.Product.Category.Name,
            i.QuantityOnHand, i.MinimumStockLevel, i.QuantityOnHand == 0 ? "Out" : i.QuantityOnHand <= i.MinimumStockLevel ? "Low" : "Healthy", i.UpdatedAt));
    }
    public static async Task<ReportPage<T>> Page<T>(IQueryable<T> query, ReportFilter filter, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(ct);
        return new(items, filter.Page, filter.PageSize, total);
    }
    public const int ExportLimit = 10000;
    private IQueryable<OrderItem> CompletedLines(DateTimeOffset start, DateTimeOffset end) => db.OrderItems.AsNoTracking()
        .Where(i => i.Order.Status == OrderStatus.Completed && i.Order.CompletedAt >= start && i.Order.CompletedAt < end);
    public async Task<SalesMetrics> SalesMetrics(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        new ReportFilter { Start = start, End = end }.Validate("dashboard");
        return await Completed(start, end).GroupBy(o => 1)
            .Select(g => new SalesMetrics(g.Sum(o => o.TotalAmount), g.Count(), g.Average(o => o.TotalAmount)))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0);
    }
    public Task<int> LegacyCompletedCount(CancellationToken ct) => db.Orders.AsNoTracking()
        .CountAsync(o => o.Status == OrderStatus.Completed && o.CompletedAt == null, ct);
    public async Task<InventoryOverview> InventoryOverview(CancellationToken ct) => await db.Inventories.AsNoTracking().GroupBy(i => 1)
        .Select(g => new InventoryOverview(g.Count(), g.Count(i => i.QuantityOnHand > 0),
            g.Count(i => i.QuantityOnHand > 0 && i.QuantityOnHand <= i.MinimumStockLevel), g.Count(i => i.QuantityOnHand == 0)))
        .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0);
    public async Task<PendingOrderOverview> PendingOrders(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 10) throw new BusinessException("invalidReportFilters");
        var pending = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Pending);
        var count = await pending.CountAsync(ct);
        var rows = await pending.OrderBy(o => o.OrderDate).ThenBy(o => o.Id).Take(limit)
            .Select(o => new PendingOrderRow(o.OrderNumber, o.OrderDate, o.TotalAmount)).ToListAsync(ct);
        return new(count, rows);
    }
    public async Task<List<StockCoverageRow>> StockCoverage(ReportFilter filter, int limit, CancellationToken ct)
    {
        var (start, end) = filter.Validate("products");
        if (limit is < 1 or > 10 || end - start < TimeSpan.FromDays(1)) throw new BusinessException("invalidReportFilters");
        var days = (decimal)(end - start).TotalDays;
        var sold = CompletedLines(start, end).GroupBy(i => i.ProductId).Select(g => new { Id = g.Key, Quantity = g.Sum(i => (long)i.Quantity) });
        var rows = from a in sold join i in db.Inventories.AsNoTracking() on a.Id equals i.ProductId
            where a.Quantity > 0
            select new { i.ProductId, i.Product.Name, i.QuantityOnHand, a.Quantity,
                Daily = a.Quantity / days, Coverage = i.QuantityOnHand * days / a.Quantity };
        return await rows.OrderBy(x => x.Coverage).ThenBy(x => x.ProductId).Take(limit)
            .Select(x => new StockCoverageRow(x.Name, x.QuantityOnHand, x.Quantity, x.Daily, x.Coverage)).ToListAsync(ct);
    }
    public static async Task<List<T>> Export<T>(IQueryable<T> query, CancellationToken ct)
    {
        // Buffer a strictly bounded result before sending headers, so over-limit errors remain valid ProblemDetails.
        var rows = await query.Take(ExportLimit + 1).ToListAsync(ct);
        if (rows.Count > ExportLimit) throw new BusinessException("reportExportLimit", 400);
        return rows;
    }
}
