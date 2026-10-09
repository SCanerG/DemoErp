using System.Globalization;
using System.Text;
using Demo.Api.Contracts;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Controllers;

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/dashboard")]
public sealed class DashboardController(ReportingService reports) : ControllerBase
{
    [HttpGet("summary")]
    public Task<DashboardSummary> Summary([FromQuery] ReportFilter filter, CancellationToken ct) => reports.Summary(filter, ct);
    [HttpGet("sales-trend")]
    public Task<SalesTrend> Trend([FromQuery] ReportFilter filter, CancellationToken ct) => reports.Trend(filter, ct);
    [HttpGet("order-status")]
    public Task<List<StatusCount>> Status([FromQuery] ReportFilter filter, CancellationToken ct) => reports.Statuses(filter, ct);
    [HttpGet("top-products")]
    public Task<List<ProductSalesRow>> Products([FromQuery] ReportFilter filter, CancellationToken ct)
    { filter.Sort = "salesValue"; filter.Descending = true; return reports.Products(filter).Take(10).ToListAsync(ct); }
    [HttpGet("top-customers")]
    public Task<List<CustomerSalesRow>> Customers([FromQuery] ReportFilter filter, CancellationToken ct)
    { filter.Sort = "salesValue"; filter.Descending = true; return reports.Customers(filter, true).Take(10).ToListAsync(ct); }
}

[ApiController, Authorize(Policy = AccessPolicies.BusinessRead), Route("api/reports")]
public sealed class ReportsController(ReportingService reports) : ControllerBase
{
    [HttpGet("sales")]
    public Task<ReportPage<SalesRow>> Sales([FromQuery] ReportFilter filter, CancellationToken ct) => ReportingService.Page(reports.Sales(filter), filter, ct);
    [HttpGet("products")]
    public Task<ReportPage<ProductSalesRow>> Products([FromQuery] ReportFilter filter, CancellationToken ct) => ReportingService.Page(reports.Products(filter), filter, ct);
    [HttpGet("customers")]
    public Task<ReportPage<CustomerSalesRow>> Customers([FromQuery] ReportFilter filter, CancellationToken ct) => ReportingService.Page(reports.Customers(filter), filter, ct);
    [HttpGet("inventory")]
    public Task<ReportPage<InventoryReportRow>> Inventory([FromQuery] ReportFilter filter, CancellationToken ct) => ReportingService.Page(reports.Inventory(filter), filter, ct);
    [HttpGet("sales/export")]
    public async Task<FileContentResult> SalesExport([FromQuery] ReportFilter filter, CancellationToken ct) => Csv("sales",
        ["Order number", "Customer", "Order date (UTC)", "Completion date (UTC)", "Status", "Item count", "Total (USD)"],
        (await ReportingService.Export(reports.Sales(filter), ct)).Select(x => new object?[] { x.OrderNumber, x.Customer, x.OrderDate, x.CompletedAt, x.Status, x.ItemCount, x.TotalAmount }));
    [HttpGet("products/export")]
    public async Task<FileContentResult> ProductsExport([FromQuery] ReportFilter filter, CancellationToken ct) => Csv("products",
        ["Product", "Category", "Quantity sold", "Completed sales (USD)", "Completed order count", "Current stock"],
        (await ReportingService.Export(reports.Products(filter), ct)).Select(x => new object?[] { x.Product, x.Category, x.QuantitySold, x.SalesValue, x.CompletedOrderCount, x.CurrentStock }));
    [HttpGet("customers/export")]
    public async Task<FileContentResult> CustomersExport([FromQuery] ReportFilter filter, CancellationToken ct) => Csv("customers",
        ["Customer", "Orders created in period", "Completed orders", "Completed sales (USD)", "Average completed order (USD)", "Last completion (UTC)"],
        (await ReportingService.Export(reports.Customers(filter), ct)).Select(x => new object?[] { x.Customer, x.TotalOrders, x.CompletedOrders, x.SalesValue, x.AverageOrderValue, x.LastCompletedAt }));
    [HttpGet("inventory/export")]
    public async Task<FileContentResult> InventoryExport([FromQuery] ReportFilter filter, CancellationToken ct) => Csv("inventory",
        ["Product", "Category", "Current quantity", "Minimum stock", "Stock status", "Updated (UTC)"],
        (await ReportingService.Export(reports.Inventory(filter), ct)).Select(x => new object?[] { x.Product, x.Category, x.QuantityOnHand, x.MinimumStockLevel, x.StockStatus, x.UpdatedAt }));
    private FileContentResult Csv(string name, string[] headers, IEnumerable<object?[]> rows)
    {
        var csv = new StringBuilder("\uFEFF"); // UTF-8 BOM helps spreadsheet applications recognize Turkish characters.
        csv.Append(string.Join(',', headers.Select(CsvCell))).Append("\r\n");
        foreach (var row in rows) csv.Append(string.Join(',', row.Select(CsvCell))).Append("\r\n");
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"{name}.csv");
    }
    public static string CsvCell(object? value)
    {
        var text = value switch { null => "", DateTimeOffset date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? "" };
        var first = text.TrimStart();
        if (first.StartsWith('=') || first.StartsWith('+') || first.StartsWith('-') || first.StartsWith('@')
            || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
