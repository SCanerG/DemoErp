using System.Text.Json;

namespace Demo.Api.AI;

public sealed record AiChatRequest(string Message, string Language);
public sealed record AiSource(string Type, string Name, string Label, JsonElement Parameters, string ReportPath);
public sealed record AiChatResponse(string Answer, string Language, DateTimeOffset GeneratedAtUtc, List<AiSource> Sources, string RequestId);
public sealed record AiStatus(bool Enabled, bool Configured, bool Available);
public sealed record AiToolDefinition(string Name, string Description, string Schema, string Policy, string Label, string ReportPath);
public sealed record AiToolCall(string Id, string Name, string Arguments);
public sealed record AiModelMessage(string Role, string Text, IReadOnlyList<AiToolCall>? Calls = null, string? CallId = null);
public sealed record AiModelTurn(string? Answer, string? Language, IReadOnlyList<AiToolCall> Calls, int? TotalTokens = null);
public interface IAiModelClient
{
    Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken ct);
}
public sealed record AiToolResult(string Source, DateTimeOffset GeneratedAtUtc, DateTimeOffset? StartDate,
    DateTimeOffset? EndDate, string Basis, string Currency, bool HasData, object Data);
public sealed record AiSales(decimal Sales, int CompletedOrders, decimal AverageOrderValue);
public sealed record AiSalesComparison(AiSales Current, AiSales? Previous, decimal? SalesChange, decimal? ChangePercent,
    DateTimeOffset? PreviousStart, DateTimeOffset? PreviousEnd, int ExcludedLegacyOrders);
public sealed record AiProduct(string Product, long QuantitySold, decimal SalesValue, int CompletedOrders, int CurrentStock);
public sealed record AiCustomer(string Customer, int CompletedOrders, decimal SalesValue, decimal AverageOrderValue);
public sealed record AiStock(string Product, int QuantityOnHand, int MinimumStockLevel, string StockStatus);
public sealed record AiInventoryOverview(int TotalProducts, int InStockProducts, int LowStockProducts, int OutOfStockProducts);
public sealed record AiPendingOrder(string OrderNumber, DateTimeOffset OrderDate, decimal TotalAmount);
public sealed record AiPendingOrders(int TotalPendingOrders, List<AiPendingOrder> Orders);
public sealed record AiStockRisk(string Product, int QuantityOnHand, long RecentCompletedQuantity, decimal DailyAverage,
    decimal EstimatedDaysRemaining);
