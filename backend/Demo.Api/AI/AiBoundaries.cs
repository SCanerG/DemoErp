using System.Text.Json;
using System.Text.RegularExpressions;
using Demo.Api.Errors;

namespace Demo.Api.AI;
public enum AiToolAccess { ReadOnly, RequiresHumanApproval }
public sealed class AiExecutionPolicy
{
    // Even a future approval token cannot enable a write tool in this read-only release.
    public void Demand(AiToolDefinition tool)
    { if (tool.Access != AiToolAccess.ReadOnly) throw new BusinessException("aiReadOnly", 403); }
}
public static partial class AiOutputValidator
{
    public const string Schema = """{"type":"object","properties":{"answer":{"type":"string"},"language":{"type":"string","enum":["en","tr"]}},"required":["answer","language"],"additionalProperties":false}""";
    public static (string Answer, string Language) Parse(string json)
    {
        try {
            if (json.Length > 50000) throw new FormatException();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
                || root.EnumerateObject().Select(p => p.Name).Distinct().Count() != 2) throw new FormatException();
            var answer = root.GetProperty("answer").GetString(); var language = root.GetProperty("language").GetString();
            if (string.IsNullOrWhiteSpace(answer) || answer.Length > 8000 || language is not ("en" or "tr")) throw new FormatException();
            return (answer, language);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw new BusinessException("aiInvalidResponse", 502); }
    }
    public static bool ContainsSecret(string value, string configuredKey) =>
        (!string.IsNullOrEmpty(configuredKey) && value.Contains(configuredKey, StringComparison.Ordinal)) || SecretPattern().IsMatch(value);
    [GeneratedRegex(@"(?i)(?:\bsk-[a-z0-9_\-]{16,}|\bBearer\s+[a-z0-9_.\-]{16,}|\bPassword\s*=\s*[^;\s]{4,})", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();
    public static void Validate(AiModelTurn turn, string language, string key)
    {
        if (turn.Calls is null || turn.TotalTokens is < 0) throw new BusinessException("aiInvalidResponse", 502);
        if (turn.Calls.Count > 0) {
            if (turn.Answer is not null || turn.Language is not null) throw new BusinessException("aiInvalidResponse", 502);
            return;
        }
        if (string.IsNullOrWhiteSpace(turn.Answer) || turn.Answer.Length > 8000 || turn.Language != language || ContainsSecret(turn.Answer, key))
            throw new BusinessException("aiInvalidResponse", 502);
    }
    public static string ToolPayload(AiToolResult result, AiToolDefinition tool, string key)
    {
        var validType = tool.Name switch {
            "get_sales_summary" => result.Data is AiSalesComparison,
            "get_top_products" => result.Data is List<AiProduct>,
            "get_top_customers" => result.Data is List<AiCustomer>,
            "get_order_status_summary" => result.Data is List<Demo.Api.Contracts.StatusCount>,
            "get_low_stock_products" or "get_out_of_stock_products" => result.Data is List<AiStock>,
            "get_inventory_overview" => result.Data is AiInventoryOverview,
            "get_pending_orders" => result.Data is AiPendingOrders,
            "get_stock_risk" => result.Data is List<AiStockRisk>, _ => false };
        if (result.Data is System.Collections.ICollection rows && rows.Count > (tool.Name == "get_order_status_summary" ? 4 : 10)) validType = false;
        if (result.Data is AiPendingOrders pending && pending.Orders.Count > 10) validType = false;
        if (!validType || result.Source != tool.Name || result.Currency != "USD") throw new BusinessException("aiToolFailure", 503);
        var payload = JsonSerializer.Serialize(result, AiAssistantService.Json);
        if (payload.Length > 20000 || ContainsSecret(payload, key)) throw new BusinessException("aiToolFailure", 503);
        return payload;
    }
}
public sealed class AiProviderException(string code, int status, TimeSpan? retryAfter = null, int? totalTokens = null, string? validationFailure = null) : BusinessException(code, status)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public int? TotalTokens { get; } = totalTokens;
    public string? ValidationFailure { get; } = validationFailure;
}
public static class AiProviderRunner
{
    internal static readonly AsyncLocal<int> RetryOrdinal = new();
    public static async Task<AiModelTurn> Complete(IAiModelClient model, IReadOnlyList<AiModelMessage> messages,
        IReadOnlyList<AiToolDefinition> tools, AiAssistantOptions options, Action attempt, CancellationToken ct)
    {
        for (var retry = 0; ; retry++) {
            ct.ThrowIfCancellationRequested(); attempt();
            var previousRetry = RetryOrdinal.Value; RetryOrdinal.Value = retry;
            try { return await model.Complete(messages, tools, ct).WaitAsync(ct); }
            catch (BusinessException ex) when (retry < options.MaxRetries && ex.Code is "aiProviderUnavailable" or "aiProviderRateLimit")
            { await Task.Delay(ex is AiProviderException { RetryAfter: { } delay } ? delay : TimeSpan.FromMilliseconds(200 * (retry + 1)), ct); }
            finally { RetryOrdinal.Value = previousRetry; }
        }
    }
}
public sealed class DevelopmentAiModelClient : IAiModelClient
{
    public Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (messages.Last().Role == "user" && tools.Any(t => t.Name == "get_inventory_overview"))
            return Task.FromResult(new AiModelTurn(null, null, [new("mock_inventory", "get_inventory_overview", "{}")], 0));
        var tr = messages[0].Text.Contains("language=tr", StringComparison.Ordinal);
        if (messages[0].Text.StartsWith("You explain verified READ-ONLY ERP facts.", StringComparison.Ordinal))
            return Task.FromResult(new AiModelTurn(tr ? "Geliştirme simülasyonu: izinli rapor verileri aşağıdaki doğrulanmış alanlarda gösteriliyor. Canlı bir dil modeli çağrılmadı."
                : "Development mock: authorized report facts are shown in the verified fields below. No live language model was called.", tr ? "tr" : "en", [], 0));
        return Task.FromResult(new AiModelTurn(tr ? "Geliştirme simülasyonu: izinli stok raporu okundu. Rakamları kaynak rapordan doğrulayın. Bu yanıt bir dil modeli tarafından üretilmedi."
            : "Development mock: the authorized inventory report was read. Verify figures in the source report. This answer was not generated by a language model.", tr ? "tr" : "en", [], 0));
    }
}
