using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Demo.Api.AI.Agents;
using Demo.Api.AI.Knowledge;
using Demo.Api.AI.Monitoring;
using Demo.Api.Contracts;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace Demo.Api.AI.Quality;

public sealed record EvaluationScenario(string Id, string Category, string Check, bool Critical, string Prompt, string UserRole,
    JsonElement Fixture, JsonElement Expected, List<string> ExpectedTools, List<string> Constraints);
public sealed record EvaluationDataset(string Version, string Suite, List<EvaluationScenario> Scenarios);
public sealed record EvaluationCaseResult(string Id, string Category, bool Critical, bool Passed, string Assertion, double DurationMs, Dictionary<string, double?> Measurements, bool ManualReviewRequired);
public sealed record EvaluationReport(string Suite, string Version, string DatasetHash, string Mode, DateTimeOffset CreatedAt,
    List<EvaluationCaseResult> Cases, int Passed, int Failed, int CriticalFailures, double DurationMs);
public sealed record EvaluationComparison(bool Comparable, List<string> NewlyFailing, List<string> Recovered, List<string> LatencyRegressions, int CriticalRegressions);
public sealed record EvaluationThresholds(double LatencyRatio = 2, double MinimumLatencyIncreaseMs = 20)
{
    public void Validate() { if (LatencyRatio is < 1.1 or > 10 || MinimumLatencyIncreaseMs is < 1 or > 60000) throw new BusinessException("qualityInvalidRequest"); }
}
public static class RetrievalQuality
{
    // Labelled ranks only; no semantic similarity is treated as evidence of factual entailment.
    public static Dictionary<string, double?> Measure(IReadOnlyList<string> ranked, IReadOnlyCollection<string> relevant, int k)
    {
        if (k is < 1 or > 20 || ranked.Distinct().Count() != ranked.Count || relevant.Distinct().Count() != relevant.Count) throw new BusinessException("qualityInvalidDataset");
        var top = ranked.Take(k).ToArray(); var matches = top.Count(relevant.Contains); var first = Array.FindIndex(top, relevant.Contains);
        return new() { ["recallAtK"] = relevant.Count == 0 ? null : matches / (double)relevant.Count,
            ["precisionAtK"] = top.Length == 0 ? null : matches / (double)top.Length, ["mrrAtK"] = relevant.Count == 0 ? null : first < 0 ? 0 : 1d / (first + 1), ["returned"] = top.Length, ["k"] = k };
    }
}
public sealed class EvaluationCatalog
{
    public string Json { get; }
    public string Hash { get; }
    public EvaluationDataset Dataset { get; }
    public EvaluationCatalog()
    {
        using var stream = typeof(EvaluationCatalog).Assembly.GetManifestResourceStream("Demo.Api.AI.Quality.contracts-v1.json") ?? throw new InvalidOperationException("Evaluation dataset missing.");
        using var reader = new StreamReader(stream); Json = reader.ReadToEnd(); Dataset = Parse(Json); Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json)));
    }
    public static EvaluationDataset Parse(string json)
    {
        try
        {
            if (json.Length > 100000) throw new FormatException();
            var dataset = JsonSerializer.Deserialize<EvaluationDataset>(json, new JsonSerializerOptions(AiAssistantService.Json) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })!;
            if (dataset.Version != "1" || dataset.Suite != "contracts-v1" || dataset.Scenarios.Count is < 1 or > 100 || dataset.Scenarios.Select(s => s.Id).Distinct().Count() != dataset.Scenarios.Count) throw new FormatException();
            foreach (var s in dataset.Scenarios)
            {
                if (QualityTelemetry.Safe(s.Id) == "redacted" || s.Prompt.Length is < 1 or > 2000 || s.UserRole is not ("Admin" or "Manager" or "Viewer" or "Anonymous")
                    || s.Category is not ("inventory" or "sales" or "order" or "knowledge" or "monitoring" or "security" or "workflow" or "provider")
                    || s.Check is not ("route" or "arguments" or "inventory" or "structured" or "numbers" or "orderDraft" or "authorization" or "writeTool" or "citation" or "retrievalRanks" or "monitoring" or "budget" or "fallback")
                    || s.Fixture.ValueKind != JsonValueKind.Object || s.Expected.ValueKind != JsonValueKind.Object || s.ExpectedTools.Count > 10 || s.ExpectedTools.Any(t => !AiToolRegistry.All.Any(x => x.Name == t) && !AgentTools.Known(t))
                    || s.Constraints.Count == 0 || s.Constraints.Count > 10) throw new FormatException();
            }
            return dataset;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or FormatException or InvalidOperationException) { throw new BusinessException("qualityInvalidDataset"); }
    }
}
public sealed class EvaluationEngine(EvaluationCatalog catalog, IAuthorizationService authorization)
{
    public async Task<EvaluationReport> Run(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew(); var cases = new List<EvaluationCaseResult>();
        foreach (var scenario in catalog.Dataset.Scenarios)
        {
            ct.ThrowIfCancellationRequested(); var time = Stopwatch.StartNew(); var measurements = new Dictionary<string, double?>(); bool passed; string assertion;
            try { passed = await Evaluate(scenario, measurements, ct); assertion = passed ? "expected_contract_verified" : "expected_contract_mismatch"; }
            catch (Exception e) when (e is not OperationCanceledException) { passed = false; assertion = "scenario_execution_failed"; }
            cases.Add(new(scenario.Id, scenario.Category, scenario.Critical, passed, assertion, time.Elapsed.TotalMilliseconds, measurements,
                scenario.Check is "numbers" or "citation" or "retrievalRanks" || scenario.Category == "sales"));
        }
        return new(catalog.Dataset.Suite, catalog.Dataset.Version, catalog.Hash, "deterministic-fixtures", DateTimeOffset.UtcNow, cases,
            cases.Count(c => c.Passed), cases.Count(c => !c.Passed), cases.Count(c => c.Critical && !c.Passed), watch.Elapsed.TotalMilliseconds);
    }
    private async Task<bool> Evaluate(EvaluationScenario s, Dictionary<string, double?> metrics, CancellationToken ct)
    {
        var f = s.Fixture; var expected = s.Expected;
        int I(string key) => f.GetProperty(key).GetInt32(); string S(string key) => f.GetProperty(key).GetString()!;
        bool Rejected(Action work) { try { work(); return false; } catch (BusinessException e) { return expected.TryGetProperty("error", out var code) && e.Code == code.GetString(); } }
        switch (s.Check)
        {
            case "route": return AgentRouting.Simple(s.Prompt) == expected.GetProperty("kind").GetString() && AgentRouting.DetectPeriod(s.Prompt) == expected.GetProperty("period").GetString();
            case "arguments":
                if (expected.TryGetProperty("error", out _)) return Rejected(() => AiToolRegistry.Validate(AiToolRegistry.Find(S("tool")), f.GetProperty("arguments").GetRawText()));
                var args = AiToolRegistry.Validate(AiToolRegistry.Find(S("tool")), f.GetProperty("arguments").GetRawText());
                return args.Limit == expected.GetProperty("limit").GetInt32() && s.ExpectedTools.Contains(S("tool"));
            case "inventory":
                var row = InventoryAnalysisAgent.Calculate(new(Guid.Empty, "Synthetic part", "Synthetic", I("stock"), I("minimum"), "Low", DateTimeOffset.UnixEpoch), I("sold"), new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(30)));
                metrics["suggestedQuantity"] = row.SuggestedQuantity;
                return row.CurrentStock == I("stock") && (expected.GetProperty("suggested").ValueKind == JsonValueKind.Null ? row.SuggestedQuantity is null : row.SuggestedQuantity == expected.GetProperty("suggested").GetInt32());
            case "structured": return Rejected(() => AiOutputValidator.Parse(S("output")));
            case "numbers": return Rejected(() => AiSummarySemantics.Validate(S("answer"), f.GetProperty("facts").GetRawText(), "en"));
            case "orderDraft":
                var draft = await new IntelligentOrderDraftAgent(new FixtureOrderReader()).Prepare(new("order", "last30days", "Synthetic customer", [new("Synthetic part", I("quantity"))]), new ClaimsPrincipal(), ct);
                metrics["total"] = (double?)draft.Total;
                return draft.CustomerId == FixtureOrderReader.Customer && draft.Lines.Single().ProductId == FixtureOrderReader.Product && draft.Total == expected.GetProperty("total").GetDecimal() && draft.NotSubmitted && !draft.Persisted && draft.RequiresHumanReview;
            case "authorization":
                var claims = s.UserRole == "Anonymous" ? new ClaimsPrincipal() : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, s.UserRole)], "evaluation"));
                return (await authorization.AuthorizeAsync(claims, null, S("policy"))).Succeeded == expected.GetProperty("allowed").GetBoolean();
            case "writeTool": return Rejected(() => new AiExecutionPolicy().Demand(new("create_order", "Synthetic unsafe model request", "{}", AccessPolicies.OrderManage, "", "", AiToolAccess.RequiresHumanApproval)));
            case "citation":
                var passages = new List<KnowledgePassage> { new(Guid.Parse(S("chunkId")), Guid.Parse(S("documentId")), I("version"), "synthetic-policy.md", "Policy", S("source"), 1) };
                if (expected.TryGetProperty("error", out _)) return Rejected(() => new KnowledgeGrounding().Verify(S("selection"), passages));
                var citations = new KnowledgeGrounding().Verify(S("selection"), passages); metrics["validCitations"] = citations.Count;
                return citations.Count == expected.GetProperty("citations").GetInt32() && citations.All(c => c.ChunkId == passages[0].ChunkId && c.Version == passages[0].Version && passages[0].Text.Contains(c.Quote, StringComparison.Ordinal));
            case "retrievalRanks":
                var ranked = f.GetProperty("rankedChunks").EnumerateArray().Select(x => x.GetString()!).ToArray(); var relevant = f.GetProperty("relevantChunks").EnumerateArray().Select(x => x.GetString()!).ToArray();
                foreach (var metric in RetrievalQuality.Measure(ranked, relevant, I("k"))) metrics[metric.Key] = metric.Value;
                return expected.EnumerateObject().All(p => metrics.TryGetValue(p.Name, out var value) && (p.Value.ValueKind == JsonValueKind.Null ? value is null : value is not null && Math.Abs(value.Value - p.Value.GetDouble()) < 0.00001));
            case "monitoring": return MonitoringAnalysisService.Significant(I("current"), I("previous"), I("samples"), I("previousSamples"), new()) == expected.GetProperty("signal").GetBoolean();
            case "budget": return Rejected(() => { var budget = new AgentBudget(); for (var i = 0; i < 7; i++) budget.Tool(); });
            case "fallback":
                var response = await AiAnswerComposer.Complete(_ => Task.FromException<AiModelTurn>(new BusinessException("aiInvalidResponse", 502)), "en", "", () => "Verified deterministic summary.", ct);
                return response.ResponseKind == "fallback" && response.TotalTokens == 0;
            default: return false;
        }
    }
    public static EvaluationComparison Compare(EvaluationReport current, EvaluationReport previous, EvaluationThresholds thresholds)
    {
        thresholds.Validate();
        if (current.DatasetHash != previous.DatasetHash || current.Mode != previous.Mode || current.Suite != previous.Suite) return new(false, [], [], [], 0);
        var old = previous.Cases.ToDictionary(c => c.Id); var changed = current.Cases.Where(c => !c.Passed && old.TryGetValue(c.Id, out var p) && p.Passed).ToList();
        return new(true, changed.Select(c => c.Id).ToList(), current.Cases.Where(c => c.Passed && old.TryGetValue(c.Id, out var p) && !p.Passed).Select(c => c.Id).ToList(),
            current.Cases.Where(c => old.TryGetValue(c.Id, out var p) && p.DurationMs > 0 && c.DurationMs / p.DurationMs >= thresholds.LatencyRatio && c.DurationMs - p.DurationMs >= thresholds.MinimumLatencyIncreaseMs).Select(c => c.Id).ToList(), changed.Count(c => c.Critical));
    }
    private sealed class FixtureOrderReader : IOrderDraftReader
    {
        public static readonly Guid Customer = Guid.Parse("11700000-0000-0000-0000-000000000001"), Product = Guid.Parse("11700000-0000-0000-0000-000000000002");
        public Task<List<DraftCandidate>> Customers(string name, ClaimsPrincipal user, CancellationToken ct) => Task.FromResult(new List<DraftCandidate> { new(Customer, "Synthetic customer") });
        public Task<List<DraftProduct>> Products(string name, Guid warehouse, ClaimsPrincipal user, CancellationToken ct) => Task.FromResult(new List<DraftProduct> { new(Product, "Synthetic part", 12.5m, 4) });
        public Task<DraftWarehouse> Warehouse(ClaimsPrincipal user, CancellationToken ct) => Task.FromResult(new DraftWarehouse(Guid.Parse("11700000-0000-0000-0000-000000000003"), "Synthetic warehouse"));
    }
}
