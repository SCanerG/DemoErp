using Demo.Api.Domain;
using Demo.Api.Errors;

namespace Demo.Api.Contracts;

public sealed class ReportFilter
{
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Sort { get; set; } = "default";
    public bool Descending { get; set; } = true;
    public Guid? CustomerId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public OrderStatus? Status { get; set; }
    public decimal? MinimumTotal { get; set; }
    public decimal? MaximumTotal { get; set; }
    public string? Search { get; set; }
    public string? StockStatus { get; set; }
    public (DateTimeOffset Start, DateTimeOffset End) Validate(string kind)
    {
        var end = (End ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var start = (Start ?? end.AddDays(-30)).ToUniversalTime();
        string[] sorts = kind switch
        {
            "sales" => ["default", "orderDate", "completedAt", "totalAmount", "customer", "orderNumber"],
            "products" => ["default", "salesValue", "quantitySold", "product", "currentStock"],
            "customers" => ["default", "salesValue", "customer", "totalOrders", "completedOrders"],
            "inventory" => ["default", "product", "quantityOnHand", "minimumStockLevel", "updatedAt"],
            _ => ["default"]
        };
        if (start >= end || end - start > TimeSpan.FromDays(366) || Page is < 1 or > 1000000 || PageSize is < 1 or > 100
            || !sorts.Contains(Sort) || MinimumTotal < 0 || MaximumTotal < 0 || MinimumTotal > MaximumTotal
            || Search?.Length > 150 || StockStatus is not (null or "Low" or "Out" or "Healthy")
            || Status.HasValue && !Enum.IsDefined(Status.Value)
            || kind == "inventory" && (Start.HasValue || End.HasValue))
            throw new BusinessException("invalidReportFilters");
        return (start, end);
    }
}
public sealed record ReportPage<T>(List<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
public sealed record DashboardSummary(decimal TotalRevenue, int TotalOrders, int CompletedOrders, int PendingOrders,
    int ActiveCustomers, int ActiveProducts, int LowStockProducts, int OutOfStockProducts, int LegacyCompletedOrders);
public sealed record TrendPoint(DateTime Period, decimal SalesValue);
public sealed record SalesTrend(string Granularity, List<TrendPoint> Items);
public sealed record StatusCount(OrderStatus Status, int Count);
public sealed record SalesRow(Guid Id, string OrderNumber, string Customer, DateTimeOffset OrderDate, DateTimeOffset? CompletedAt,
    OrderStatus Status, int ItemCount, decimal TotalAmount);
public sealed record ProductSalesRow(Guid Id, string Product, string Category, long QuantitySold, decimal SalesValue, int CompletedOrderCount, int CurrentStock);
public sealed record CustomerSalesRow(Guid Id, string Customer, int TotalOrders, int CompletedOrders, decimal SalesValue, decimal AverageOrderValue, DateTimeOffset? LastCompletedAt);
public sealed record InventoryReportRow(Guid Id, string Product, string Category, int QuantityOnHand, int MinimumStockLevel, string StockStatus, DateTimeOffset UpdatedAt);
public sealed record SalesMetrics(decimal Sales, int CompletedOrders, decimal AverageOrderValue);
public sealed record InventoryOverview(int TotalProducts, int InStockProducts, int LowStockProducts, int OutOfStockProducts);
public sealed record PendingOrderRow(string OrderNumber, DateTimeOffset OrderDate, decimal TotalAmount);
public sealed record PendingOrderOverview(int TotalPendingOrders, List<PendingOrderRow> Orders);
public sealed record StockCoverageRow(string Product, int QuantityOnHand, long RecentCompletedQuantity, decimal DailyAverage, decimal EstimatedDaysRemaining);
