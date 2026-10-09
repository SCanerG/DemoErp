using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Api.Contracts;
using Demo.Api.Controllers;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Api.Tests;

public sealed class ReportingTests(ApiFixture fixture) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    private HttpClient client = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset Start = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddDays(3);
    private static string Range => $"start={Uri.EscapeDataString(Start.ToString("O"))}&end={Uri.EscapeDataString(End.ToString("O"))}";
    public async ValueTask InitializeAsync()
    {
        client = fixture.App.CreateClient();
        using var scope = fixture.App.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var email = $"report-{Guid.NewGuid():N}@example.com";
        await auth.Register(new("Report viewer", email, "ReportTestPassword123!"), Ct);
        var login = await auth.Login(new(email, "ReportTestPassword123!"), Ct);
        client.DefaultRequestHeaders.Authorization = new("Bearer", login!.AccessToken);
    }
    public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    private async Task<(Guid Product, Guid Customer)> Seed()
    {
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Name = "Reports" };
        var product = new Product { Name = "=ÜRÜN, \"özel\"\nSatır", CategoryId = category.Id, Price = 999m };
        var customer = new Customer { Name = "@Müşteri" };
        db.AddRange(category, product, customer);
        foreach (var (status, completion, total) in new (OrderStatus, DateTimeOffset?, decimal)[] {
            (OrderStatus.Completed, Start, 20), (OrderStatus.Completed, Start.AddDays(2), 30),
            (OrderStatus.Completed, End, 40), (OrderStatus.Completed, Start.AddTicks(-1), 50),
            (OrderStatus.Completed, null, 60), (OrderStatus.Pending, null, 70), (OrderStatus.Cancelled, null, 80) })
        {
            var order = new Order { CustomerId = customer.Id, OrderNumber = $"R-{Guid.NewGuid():N}", Status = status,
                OrderDate = Start.AddHours(1), CompletedAt = completion, TotalAmount = total,
                Items = [new OrderItem { ProductId = product.Id, Quantity = 2, UnitPrice = total / 2, LineTotal = total }] };
            db.Orders.Add(order);
        }
        await db.SaveChangesAsync(Ct);
        var inventory = await db.Inventories.SingleAsync(i => i.ProductId == product.Id, Ct);
        inventory.QuantityOnHand = 3; inventory.MinimumStockLevel = 5;
        await db.SaveChangesAsync(Ct);
        return (product.Id, customer.Id);
    }
    [Fact]
    public async Task Aggregates_use_completion_boundaries_stored_prices_and_current_snapshots()
    {
        var before = await client.GetFromJsonAsync<DashboardSummary>($"/api/dashboard/summary?{Range}", Ct);
        var seed = await Seed();
        var products = await client.GetFromJsonAsync<ReportPage<ProductSalesRow>>($"/api/reports/products?{Range}&productId={seed.Product}", Ct);
        var product = Assert.Single(products!.Items);
        Assert.Equal(4, product.QuantitySold); Assert.Equal(50, product.SalesValue); Assert.Equal(2, product.CompletedOrderCount); Assert.Equal(3, product.CurrentStock);
        var customers = await client.GetFromJsonAsync<ReportPage<CustomerSalesRow>>($"/api/reports/customers?{Range}&customerId={seed.Customer}", Ct);
        var customer = Assert.Single(customers!.Items);
        Assert.Equal(7, customer.TotalOrders); Assert.Equal(2, customer.CompletedOrders); Assert.Equal(50, customer.SalesValue); Assert.Equal(25, customer.AverageOrderValue); Assert.Equal(Start.AddDays(2), customer.LastCompletedAt);
        foreach (var path in new[] { "summary", "sales-trend", "order-status", "top-products", "top-customers" })
        {
            var response = await client.GetAsync($"/api/dashboard/{path}?{Range}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var summary = await client.GetFromJsonAsync<DashboardSummary>($"/api/dashboard/summary?{Range}", Ct);
        Assert.Equal(before!.TotalRevenue + 50, summary!.TotalRevenue);
        Assert.Equal(before.CompletedOrders + 2, summary.CompletedOrders);
        Assert.Equal(before.TotalOrders + 7, summary.TotalOrders);
        Assert.Equal(before.PendingOrders + 1, summary.PendingOrders);
        Assert.Equal(before.LegacyCompletedOrders + 1, summary.LegacyCompletedOrders);
        var trend = await client.GetFromJsonAsync<SalesTrend>($"/api/dashboard/sales-trend?{Range}", Ct);
        Assert.Equal("day", trend!.Granularity); Assert.Equal(3, trend.Items.Count); Assert.Equal(0, trend.Items[1].SalesValue);
        Assert.True(trend.Items[0].SalesValue >= 20); Assert.True(trend.Items[2].SalesValue >= 30);
        var statuses = await client.GetFromJsonAsync<List<StatusCount>>($"/api/dashboard/order-status?{Range}", Json, Ct);
        Assert.True(statuses!.Single(x => x.Status == OrderStatus.Completed).Count >= 5);
        var stock = await client.GetFromJsonAsync<ReportPage<InventoryReportRow>>($"/api/reports/inventory?stockStatus=Low&categoryId={await Category(seed.Product)}", Ct);
        Assert.Equal("Low", Assert.Single(stock!.Items).StockStatus);
    }
    private async Task<Guid?> Category(Guid product)
    {
        using var scope = fixture.App.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.Where(p => p.Id == product).Select(p => p.CategoryId).SingleAsync(Ct);
    }
    [Fact]
    public async Task Sales_filter_paging_count_and_ordering_are_stable()
    {
        var seed = await Seed();
        var url = $"/api/reports/sales?{Range}&customerId={seed.Customer}&sort=totalAmount&descending=false&pageSize=1";
        var first = await client.GetFromJsonAsync<ReportPage<SalesRow>>(url, Json, Ct);
        var second = await client.GetFromJsonAsync<ReportPage<SalesRow>>(url + "&page=2", Json, Ct);
        Assert.Equal(7, first!.TotalCount); Assert.Equal(7, first.TotalPages); Assert.Equal(20, Assert.Single(first.Items).TotalAmount);
        Assert.Equal(30, Assert.Single(second!.Items).TotalAmount);
        Assert.Equal(first.Items[0].Id, (await client.GetFromJsonAsync<ReportPage<SalesRow>>(url, Json, Ct))!.Items[0].Id);
        var filtered = await client.GetFromJsonAsync<ReportPage<SalesRow>>($"/api/reports/sales?{Range}&customerId={seed.Customer}&status=Completed&minimumTotal=30&maximumTotal=40", Json, Ct);
        Assert.Equal(2, filtered!.TotalCount);
    }
    [Fact]
    public async Task Empty_and_monthly_results_are_valid()
    {
        const string range = "start=1990-01-01T00:00:00Z&end=1990-05-01T00:00:00Z";
        var summary = await client.GetFromJsonAsync<DashboardSummary>("/api/dashboard/summary?" + range, Ct);
        Assert.Equal(0, summary!.TotalRevenue); Assert.Equal(0, summary.CompletedOrders);
        var trend = await client.GetFromJsonAsync<SalesTrend>("/api/dashboard/sales-trend?" + range, Ct);
        Assert.Equal("month", trend!.Granularity); Assert.Equal(4, trend.Items.Count); Assert.All(trend.Items, x => Assert.Equal(0, x.SalesValue));
        var products = await client.GetFromJsonAsync<ReportPage<ProductSalesRow>>("/api/reports/products?" + range, Ct);
        Assert.Empty(products!.Items); Assert.Equal(0, products.TotalPages);
    }
    [Fact]
    public async Task Monthly_sales_group_actual_completions_across_month_boundaries()
    {
        const string range = "start=2024-01-01T00:00:00Z&end=2024-06-01T00:00:00Z";
        var before = await client.GetFromJsonAsync<SalesTrend>("/api/dashboard/sales-trend?" + range, Ct);
        await Seed();
        var after = await client.GetFromJsonAsync<SalesTrend>("/api/dashboard/sales-trend?" + range, Ct);
        Assert.Equal("month", after!.Granularity); Assert.Equal(5, after.Items.Count);
        Assert.Equal(before!.Items[0].SalesValue + 50, after.Items[0].SalesValue);
        Assert.Equal(before.Items[1].SalesValue + 90, after.Items[1].SalesValue);
        Assert.Equal(0, after.Items[2].SalesValue);
        Assert.Equal(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), after.Items[1].Period);
    }
    [Theory]
    [InlineData("sales", "start=2024-02-02Z&end=2024-01-01Z")]
    [InlineData("sales", "start=2020-01-01T00:00:00Z&end=2024-01-01T00:00:00Z")]
    [InlineData("sales", "page=0")][InlineData("sales", "pageSize=101")][InlineData("sales", "sort=drop table")]
    [InlineData("sales", "minimumTotal=-1")][InlineData("sales", "minimumTotal=5&maximumTotal=4")]
    [InlineData("inventory", "stockStatus=Unknown")][InlineData("inventory", "start=2024-01-01T00:00:00Z")]
    public async Task Invalid_filters_return_400(string kind, string query) => Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/reports/{kind}?{query}", Ct)).StatusCode);
    [Theory]
    [InlineData("dashboard/summary")][InlineData("dashboard/sales-trend")][InlineData("dashboard/order-status")][InlineData("dashboard/top-products")][InlineData("dashboard/top-customers")]
    [InlineData("reports/sales")][InlineData("reports/products")][InlineData("reports/customers")][InlineData("reports/inventory")]
    [InlineData("reports/sales/export")][InlineData("reports/products/export")][InlineData("reports/customers/export")][InlineData("reports/inventory/export")]
    public async Task Anonymous_read_and_exports_require_401(string path)
    {
        using var anonymous = fixture.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/" + path, Ct)).StatusCode);
    }
    [Fact]
    public async Task Viewer_exports_respect_filters_escape_quotes_newlines_and_preserve_Turkish()
    {
        var seed = await Seed();
        var response = await client.GetAsync($"/api/reports/products/export?{Range}&productId={seed.Product}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal(new byte[] { 239, 187, 191 }, bytes[..3]);
        var csv = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"'=ÜRÜN, \"\"özel\"\"\nSatır\"", csv); Assert.Contains("\"50.00\"", csv);
        foreach (var kind in new[] { "sales", "customers", "inventory" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/reports/{kind}/export?customerId={seed.Customer}&categoryId={await Category(seed.Product)}", Ct)).StatusCode);
    }
    [Theory]
    [InlineData("=SUM(1,2)")][InlineData(" +CMD")][InlineData("-1+2")][InlineData("@A1")][InlineData("\tvalue")][InlineData("\rvalue")][InlineData("\nvalue")]
    public void Csv_formula_prefix_is_neutralized(string value) => Assert.StartsWith("\"'", ReportsController.CsvCell(value));
    [Fact]
    public async Task Completion_sets_timestamp_once_atomically_with_audit_and_preserves_stock()
    {
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { Name = "Completion lifecycle" };
        var order = new Order { Customer = customer, OrderNumber = $"C-{Guid.NewGuid():N}", Status = OrderStatus.Confirmed };
        db.Orders.Add(order); await db.SaveChangesAsync(Ct);
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var email = $"completion-{Guid.NewGuid():N}@example.com";
        var user = await auth.Register(new("Completion manager", email, "CompletionTestPassword123!"), Ct);
        (await db.Users.SingleAsync(u => u.Id == user!.Id, Ct)).Role = UserRole.Manager; await db.SaveChangesAsync(Ct);
        var login = await auth.Login(new(email, "CompletionTestPassword123!"), Ct);
        using var manager = fixture.App.CreateClient(); manager.DefaultRequestHeaders.Authorization = new("Bearer", login!.AccessToken);
        var before = DateTimeOffset.UtcNow;
        var response = await manager.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Completed" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = await response.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct);
        Assert.NotNull(completed!.CompletedAt); Assert.InRange(completed.CompletedAt.Value, before, DateTimeOffset.UtcNow);
        var repeated = await manager.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Completed" }, Ct);
        Assert.Equal(completed.CompletedAt, (await repeated.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct))!.CompletedAt);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == order.Id && a.Action == "OrderComplete", Ct));
        Assert.False(await db.InventoryMovements.AnyAsync(m => m.ReferenceId == order.Id, Ct));
    }
    [Fact]
    public async Task Stable_secondary_id_pagination_handles_equal_dates_and_totals()
    {
        var seed = await Seed();
        var url = $"/api/reports/sales?{Range}&customerId={seed.Customer}&sort=orderDate&pageSize=3";
        var a = await client.GetFromJsonAsync<ReportPage<SalesRow>>(url, Json, Ct);
        var b = await client.GetFromJsonAsync<ReportPage<SalesRow>>(url + "&page=2", Json, Ct);
        Assert.Empty(a!.Items.Select(x => x.Id).Intersect(b!.Items.Select(x => x.Id)));
        Assert.Equal(a.Items.Select(x => x.Id), (await client.GetFromJsonAsync<ReportPage<SalesRow>>(url, Json, Ct))!.Items.Select(x => x.Id));
    }
    [Fact]
    public async Task Equivalent_offset_boundaries_return_identical_completed_sales()
    {
        var seed = await Seed();
        var offsetRange = $"start={Uri.EscapeDataString(Start.ToOffset(TimeSpan.FromHours(3)).ToString("O"))}&end={Uri.EscapeDataString(End.ToOffset(TimeSpan.FromHours(3)).ToString("O"))}";
        var a = await client.GetFromJsonAsync<ReportPage<ProductSalesRow>>($"/api/reports/products?{Range}&productId={seed.Product}", Ct);
        var b = await client.GetFromJsonAsync<ReportPage<ProductSalesRow>>($"/api/reports/products?{offsetRange}&productId={seed.Product}", Ct);
        Assert.Equal(a!.Items, b!.Items);
    }
    [Fact]
    public async Task Exports_reject_more_than_the_bounded_limit_before_writing_csv()
    {
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prefix = $"export-limit-{Guid.NewGuid():N}";
        db.Customers.AddRange(Enumerable.Range(0, ReportingService.ExportLimit + 1).Select(i => new Customer { Name = prefix + i }));
        await db.SaveChangesAsync(Ct);
        var response = await client.GetAsync($"/api/reports/customers/export?search={prefix}", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reportExportLimit", await response.Content.ReadAsStringAsync(Ct));
    }
}
