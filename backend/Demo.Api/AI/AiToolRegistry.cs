using System.Text.Json;
using Demo.Api.Errors;
using Demo.Api.Services;

namespace Demo.Api.AI;

public static class AiToolRegistry
{
    private const string Dates = "\"startDate\":{\"type\":[\"string\",\"null\"],\"description\":\"ISO 8601 with timezone, inclusive. Both dates null means last 30 days.\"},\"endDate\":{\"type\":[\"string\",\"null\"],\"description\":\"ISO 8601 with timezone, exclusive. Maximum 366 days.\"}";
    private const string Limit = "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}";
    private static AiToolDefinition Define(string name, string description, string properties, string required, string label, string path) =>
        new(name, description, "{\"type\":\"object\",\"properties\":{" + properties + "},\"required\":[" + required + "],\"additionalProperties\":false}", AccessPolicies.BusinessRead, label, path);
    public static IReadOnlyList<AiToolDefinition> All { get; } = Array.AsReadOnly(new[] {
        Define("get_sales_summary", "Completed sales and server-calculated averages. Optional equal preceding-period comparison. Excludes legacy orders without completion time.", Dates + ",\"comparePrevious\":{\"type\":\"boolean\"}", "\"startDate\",\"endDate\",\"comparePrevious\"", "Sales summary", "/reports/sales"),
        Define("get_top_products", "Rank products by stored completed-order sales value during completion period; stock is current.", Dates + "," + Limit, "\"startDate\",\"endDate\",\"limit\"", "Top selling products", "/reports/products"),
        Define("get_order_status_summary", "Current status distribution of orders CREATED in this period, not completion-date totals. For completed totals use get_sales_summary.", Dates, "\"startDate\",\"endDate\"", "Order status", "/reports/sales"),
        Define("get_low_stock_products", "Current positive low stock; use get_out_of_stock_products for zero stock.", Limit, "\"limit\"", "Low stock products", "/reports/inventory"),
        Define("get_out_of_stock_products", "Current zero-stock products.", Limit, "\"limit\"", "Out of stock products", "/reports/inventory"),
        Define("get_top_customers", "Pseudonymous customer ranking by completed sales during completion period. No customer names or contact information.", Dates + "," + Limit, "\"startDate\",\"endDate\",\"limit\"", "Top customers", "/reports/customers"),
        Define("get_inventory_overview", "Current total, in-stock, low-stock and zero-stock product counts.", "", "", "Inventory overview", "/reports/inventory"),
        Define("get_pending_orders", "All-time CURRENT pending orders waiting for confirmation: count and bounded oldest orders; no customer information.", Limit, "\"limit\"", "Pending orders", "/orders"),
        Define("get_stock_risk", "Current stock coverage estimate based on recent completed quantities. Estimate assumes constant demand; excludes zero-demand products. Not a forecast.", Dates + "," + Limit, "\"startDate\",\"endDate\",\"limit\"", "Stock coverage estimate", "/reports/inventory") });

    public static AiToolDefinition Find(string name) => All.SingleOrDefault(t => t.Name == name) ?? throw new BusinessException("aiUnknownTool", 400);
    public static AiArguments Validate(AiToolDefinition tool, string arguments)
    {
        if (arguments.Length > 2000) throw new BusinessException("aiInvalidArguments");
        try
        {
            using var document = JsonDocument.Parse(arguments, new JsonDocumentOptions { MaxDepth = 4 });
            using var schema = JsonDocument.Parse(tool.Schema);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException();
            var expected = schema.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
            var actual = root.EnumerateObject().Select(x => x.Name).ToList();
            if (actual.Count != expected.Count || !expected.SetEquals(actual)) throw new FormatException();
            var limit = 10;
            if (expected.Contains("limit") && (!root.GetProperty("limit").TryGetInt32(out limit) || limit is < 1 or > 10)) throw new FormatException();
            var compare = expected.Contains("comparePrevious") && root.GetProperty("comparePrevious").GetBoolean();
            DateTimeOffset? start = null, end = null;
            if (expected.Contains("startDate"))
            {
                var s = root.GetProperty("startDate"); var e = root.GetProperty("endDate");
                if (s.ValueKind != JsonValueKind.Null || e.ValueKind != JsonValueKind.Null)
                {
                    start = ParseDate(s); end = ParseDate(e);
                }
                end ??= DateTimeOffset.UtcNow; start ??= end.Value.AddDays(-30);
                if (start >= end || end - start > TimeSpan.FromDays(366) || start.Value.Year < 2000 || end > DateTimeOffset.UtcNow.AddDays(366)) throw new FormatException();
            }
            return new(start, end, limit, compare);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        { throw new BusinessException("aiInvalidArguments"); }
    }
    private static DateTimeOffset ParseDate(JsonElement value)
    {
        var text = value.GetString() ?? throw new FormatException();
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || !DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)) throw new FormatException();
        return date.ToUniversalTime();
    }
}
public sealed record AiArguments(DateTimeOffset? StartDate, DateTimeOffset? EndDate, int Limit, bool ComparePrevious);
