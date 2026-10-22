using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Demo.Api.AI.Quality;
using Demo.Api.Controllers;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Demo.Api.Tests;

public sealed class AiEvaluationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Deterministic_reviewed_evaluation_suite_and_machine_readable_report()
    {
        using var scope = fixture.App.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<EvaluationEngine>().Run(TestContext.Current.CancellationToken);
        if (Environment.GetEnvironmentVariable("AI_EVAL_REPORT_DIRECTORY") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "evaluation-report.json"), JsonSerializer.Serialize(report, AiAssistantService.Json), TestContext.Current.CancellationToken);
            var xml = new XElement("testsuite", new XAttribute("name", report.Suite), new XAttribute("tests", report.Cases.Count), new XAttribute("failures", report.Failed),
                report.Cases.Select(c => new XElement("testcase", new XAttribute("name", c.Id), new XAttribute("classname", c.Category), new XAttribute("time", (c.DurationMs / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)), c.Passed ? null : new XElement("failure", c.Assertion))));
            await File.WriteAllTextAsync(Path.Combine(output, "evaluation-junit.xml"), xml.ToString(), TestContext.Current.CancellationToken);
            if (Environment.GetEnvironmentVariable("AI_EVAL_BASELINE") is { Length: > 0 } baseline)
            {
                var previous = JsonSerializer.Deserialize<EvaluationReport>(await File.ReadAllTextAsync(baseline, TestContext.Current.CancellationToken), AiAssistantService.Json)!;
                var comparison = EvaluationEngine.Compare(report, previous, new());
                await File.WriteAllTextAsync(Path.Combine(output, "evaluation-comparison.json"), JsonSerializer.Serialize(comparison, AiAssistantService.Json), TestContext.Current.CancellationToken);
                Assert.True(comparison.Comparable, "Baseline dataset/version/mode must match."); Assert.Equal(0, comparison.CriticalRegressions);
            }
        }
        Assert.Equal(0, report.CriticalFailures); Assert.True(report.Failed == 0, string.Join(", ", report.Cases.Where(c => !c.Passed).Select(c => c.Id + ":" + c.Assertion)));
        Assert.Equal(33, report.Cases.Count); Assert.Equal("deterministic-fixtures", report.Mode);
    }
}

public sealed class AiQualityTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class Model : IAiModelClient
    {
        public Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken ct)
            => Task.FromResult(new AiModelTurn("Review the verified inventory and sales before making a decision.", "en", [], 12));
    }
    private sealed class FailingModel(int failures) : IAiModelClient
    {
        private int remaining=failures;
        public Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages,IReadOnlyList<AiToolDefinition> tools,CancellationToken ct) => remaining-- > 0 ? Task.FromException<AiModelTurn>(new AiProviderException("aiProviderUnavailable",503)) : Task.FromResult(new AiModelTurn("Verified synthetic response.","en",[],7));
    }
    private WebApplicationFactory<Program> App(bool unavailable=false) => fixture.App.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
        s.RemoveAll<QualityOptions>(); s.AddSingleton(new QualityOptions { Enabled = true, RetentionDays = 1, MaxSpans = 100, MaxEvaluationRuns = 10 });
        foreach (var service in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(QualityTelemetryWorker)).ToList()) s.Remove(service);
        s.RemoveAll<AiAssistantOptions>(); s.AddSingleton(new AiAssistantOptions { Enabled = true, Provider = "Mock", Model = "quality-test", AllowDevelopmentMock = true });
        s.RemoveAll<IAiModelClient>(); s.AddScoped<IAiModelClient>(sp => new ObservedModelClient(unavailable ? new FailingModel(10) : new Model(), sp.GetRequiredService<QualityTelemetry>(), sp.GetRequiredService<AiAssistantOptions>()));
    }));
    private static async Task<(HttpClient Client, Guid User)> Account(WebApplicationFactory<Program> app, Demo.Api.Domain.UserRole role = Demo.Api.Domain.UserRole.Admin)
    {
        using var scope = app.Services.CreateScope(); var auth = scope.ServiceProvider.GetRequiredService<AuthService>(); var email = Guid.NewGuid() + "@example.com";
        await auth.Register(new("Quality test", email, "QualityTestPassword123!"), Ct); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email, Ct); user.Role = role; await db.SaveChangesAsync(Ct);
        var token = (await auth.Login(new(email, "QualityTestPassword123!"), Ct))!.AccessToken; var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", token); return (client, user.Id);
    }
    private static async Task Flush(WebApplicationFactory<Program> app)
    {
        for (var i = 0; i < 3; i++) await app.Services.GetRequiredService<QualityTelemetry>().Flush(app.Services.GetRequiredService<IServiceScopeFactory>(), Ct);
    }
    [Fact]
    public async Task Request_orchestration_agent_tool_provider_spans_propagate_W3C_trace_and_private_execution()
    {
        await using var app = App(); var user = await Account(app);
        var created = await user.Client.PostAsJsonAsync("/api/ai/agents/runs", new AgentStartRequest("collaboration"), Ct); var execution = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var trace = "11700000000000000000000000000001"; using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/agents/runs/{execution}/execute") { Content = JsonContent.Create(new AgentInput("Analyze sales and inventory", "en", "last30days")) };
        request.Headers.Add("traceparent", "00-" + trace + "-1170000000000001-01"); var response = await user.Client.SendAsync(request, Ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(trace, response.Headers.GetValues("X-AI-Trace-Id").Single()); await Flush(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var rows = await db.Set<QualitySpanRow>().Where(s => s.TraceId == trace).ToListAsync(Ct);
        Assert.Contains(rows, r => r.Operation == "request" && r.UserId == user.User && r.ExecutionId == execution);
        Assert.Contains(rows, r => r.Operation == "orchestration" && r.ExecutionId == execution);
        var agent = Assert.Single(rows, r => r.Operation == "agent" && r.Component == "sales"); Assert.Contains(rows, r => r.Operation == "tool" && r.ParentSpanId == agent.SpanId);
        Assert.Contains(rows, r => r.Operation == "provider" && r.Tokens == 12 && r.Provider == "Mock"); Assert.All(rows, r => Assert.Equal(user.User, r.UserId));
        Assert.Contains(rows, r => r.ParentSpanId == rows.Single(x => x.Operation == "orchestration").SpanId);
    }
    [Fact]
    public async Task Trace_metadata_excludes_questions_baggage_keys_arguments_and_exception_messages()
    {
        await using var app = App(); var user = await Account(app); var telemetry = app.Services.GetRequiredService<QualityTelemetry>();
        var secret = "sk-" + new string('x', 30); string traceId;
        using (var root = new Activity("untrusted-parent").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            root.AddBaggage("secret", secret); root.SetTag("prompt", "Synthetic confidential customer"); traceId = root.TraceId.ToHexString();
            using var span = telemetry.Begin("tool", secret, user: user.User); span!.Provider("Ollama", secret); span.Fail(new Exception(secret));
        }
        await Flush(app); var body = await user.Client.GetStringAsync("/api/quality/traces/" + traceId, Ct);
        Assert.DoesNotContain(secret, body); Assert.DoesNotContain("confidential", body); Assert.DoesNotContain("baggage", body); Assert.Contains("redacted", body);
    }
    [Fact]
    public async Task Dashboard_trace_and_evaluation_access_is_owner_private_even_between_admins()
    {
        await using var app = App(); var a = await Account(app); var b = await Account(app); var viewer = await Account(app, Demo.Api.Domain.UserRole.Viewer);
        string trace; using (var span = app.Services.GetRequiredService<QualityTelemetry>().Begin("request", "Synthetic", user: a.User)) { trace = Activity.Current!.TraceId.ToHexString(); }
        await Flush(app); Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync("/api/quality/traces/" + trace, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync("/api/quality/overview?scope=system", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync("/api/quality/evaluations", new EvaluationRequest(Guid.NewGuid()), Ct)).StatusCode);
        var run = await a.Client.PostAsJsonAsync("/api/quality/evaluations", new EvaluationRequest(Guid.NewGuid()), Ct); Assert.Equal(HttpStatusCode.Accepted, run.StatusCode);
        var id = (await run.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid(); Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync($"/api/quality/evaluations/{id}", Ct)).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quality/overview", Ct)).StatusCode);
    }
    [Fact]
    public async Task Evaluations_are_idempotent_reportable_and_comparable_without_any_business_write()
    {
        await using var app = App(); var user = await Account(app); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = await db.Orders.CountAsync(Ct); var proposals = await db.Set<AiOrderProposal>().CountAsync(Ct); var key = Guid.NewGuid();
        var first = await user.Client.PostAsJsonAsync("/api/quality/evaluations", new EvaluationRequest(key), Ct); var id = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var repeat = await user.Client.PostAsJsonAsync("/api/quality/evaluations", new EvaluationRequest(key), Ct); Assert.Equal(id, (await repeat.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
        var report = await user.Client.GetFromJsonAsync<JsonElement>($"/api/quality/evaluations/{id}", Ct); Assert.Equal(0, report.GetProperty("report").GetProperty("failed").GetInt32());
        var comparison = await user.Client.GetFromJsonAsync<JsonElement>($"/api/quality/evaluations/{id}/comparison/{id}", Ct); Assert.True(comparison.GetProperty("comparable").GetBoolean());
        Assert.Equal(orders, await db.Orders.CountAsync(Ct)); Assert.Equal(proposals, await db.Set<AiOrderProposal>().CountAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/quality/traces", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/quality/overview", Ct)).StatusCode); Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task Telemetry_retention_and_row_caps_remove_expired_rows_and_keep_private_queries_bounded()
    {
        await using var app = App(); var user = await Account(app); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expired = new QualitySpanRow { UserId = user.User, TraceId = "11700000000000000000000000000000", SpanId = "1170000000000000", Operation = "request", Component = "expired", StartedAt = DateTimeOffset.UtcNow.AddDays(-2), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }; db.Add(expired); await db.SaveChangesAsync(Ct);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync("/api/quality/traces/" + expired.TraceId, Ct)).StatusCode);
        await QualityTelemetry.Purge(db, new QualityOptions { MaxSpans = 100 }, DateTimeOffset.UtcNow, Ct); Assert.False(await db.Set<QualitySpanRow>().AnyAsync(s => s.Id == expired.Id, Ct));
        var telemetry = app.Services.GetRequiredService<QualityTelemetry>(); for (var i = 0; i < 110; i++) { using var span = telemetry.Begin("tool", "synthetic", user: user.User); }
        await Flush(app); Assert.True(await db.Set<QualitySpanRow>().CountAsync(Ct) <= 100);
    }
    [Fact]
    public async Task Provider_outage_is_traced_as_failure_without_fabricated_success_and_ERP_stays_available()
    {
        await using var app=App(unavailable:true);var user=await Account(app);
        var response=await user.Client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Analyze inventory","en"),Ct);Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await user.Client.GetAsync("/api/products",Ct)).StatusCode);await Flush(app);
        using var scope=app.Services.CreateScope();var rows=await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<QualitySpanRow>().Where(s=>s.UserId==user.User).ToListAsync(Ct);
        Assert.Contains(rows,r=>r.Operation=="provider"&&r.Error=="aiProviderUnavailable"&&r.Outcome=="failure");Assert.DoesNotContain(rows,r=>r.Operation=="request"&&r.Outcome=="success");
    }
    [Fact]
    public async Task Provider_retry_ordinal_and_missing_token_usage_are_recorded_without_inventing_counts()
    {
        await using var app=App();var user=await Account(app);var telemetry=app.Services.GetRequiredService<QualityTelemetry>();
        var settings=new AiAssistantOptions{Provider="OpenAI",Model="synthetic-fixture",MaxRetries=1};
        using(var root=telemetry.Begin("request","fixture",user:user.User)) {
            var turn=await AiProviderRunner.Complete(new ObservedModelClient(new FailingModel(1),telemetry,settings),[new("user","Synthetic fixture")],[],settings,()=>{},Ct);Assert.Equal(7,turn.TotalTokens);
        }
        await Flush(app);using var scope=app.Services.CreateScope();var rows=await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<QualitySpanRow>().Where(s=>s.UserId==user.User&&s.Operation=="provider").OrderBy(s=>s.StartedAt).ToListAsync(Ct);
        Assert.Equal(2,rows.Count);Assert.Equal(0,rows[0].Retry);Assert.Null(rows[0].Tokens);Assert.Equal(1,rows[1].Retry);Assert.Equal(7,rows[1].Tokens);
    }
    [Fact]
    public async Task Dashboard_rates_distinguish_timeout_cancellation_and_completed_requests_without_faking_usage()
    {
        await using var app=App();var user=await Account(app);var telemetry=app.Services.GetRequiredService<QualityTelemetry>();
        using(var timeout=telemetry.Begin("request","fixture",user:user.User))timeout!.Fail(new BusinessException("aiTimeout",504));
        using(var cancelled=telemetry.Begin("request","fixture",user:user.User))cancelled!.Fail(new BusinessException("aiCancelled",499));
        using(var success=telemetry.Begin("request","fixture",user:user.User))success!.Outcome("success");
        await Flush(app);var value=await user.Client.GetFromJsonAsync<JsonElement>("/api/quality/overview",Ct);var metrics=value.GetProperty("metrics");
        Assert.Equal(3,metrics.GetProperty("requests").GetInt32());Assert.Equal(1d/3,metrics.GetProperty("timeoutRate").GetDouble());Assert.Equal(1d/3,metrics.GetProperty("cancellationRate").GetDouble());Assert.Equal(1d/3,metrics.GetProperty("successRate").GetDouble());
        Assert.Equal(JsonValueKind.Null,metrics.GetProperty("reportedTotalTokens").ValueKind);Assert.Equal(JsonValueKind.Null,metrics.GetProperty("latency").GetProperty("p95Ms").ValueKind);
    }
}

public sealed class AiQualityUnitTests
{
    [Fact]
    public void Dataset_rejects_duplicate_scenarios_unknown_tools_and_invalid_shape()
    {
        var catalog = new EvaluationCatalog(); var node = System.Text.Json.Nodes.JsonNode.Parse(catalog.Json)!;
        node["scenarios"]!.AsArray().Add(node["scenarios"]![0]!.DeepClone()); Assert.Throws<BusinessException>(() => EvaluationCatalog.Parse(node.ToJsonString()));
        Assert.Throws<BusinessException>(() => EvaluationCatalog.Parse("{}"));
        Assert.Throws<BusinessException>(() => EvaluationCatalog.Parse(catalog.Json.Replace("get_low_stock_products", "execute_sql", StringComparison.Ordinal)));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(19)]
    public void Small_runtime_samples_do_not_claim_percentiles(int count)
    {
        var result = QualityMetrics.Latency(Enumerable.Range(0, count).Select(x => (double)x)); Assert.Null(result.P50Ms); Assert.Null(result.P95Ms); Assert.Null(result.P99Ms);
    }
    [Fact]
    public void Percentile_floors_and_known_values_are_measurable()
    {
        var result = QualityMetrics.Latency(Enumerable.Range(1, 1000).Select(x => (double)x)); Assert.Equal(500, result.P50Ms); Assert.Equal(950, result.P95Ms); Assert.Equal(990, result.P99Ms);
    }
    [Fact]
    public void Labelled_retrieval_metrics_do_not_treat_missing_labels_as_perfect_quality()
    {
        var result = RetrievalQuality.Measure(["wrong", "right"], ["right"], 2); Assert.Equal(1, result["recallAtK"]); Assert.Equal(.5, result["precisionAtK"]); Assert.Equal(.5, result["mrrAtK"]);
        Assert.Null(RetrievalQuality.Measure([], [], 5)["recallAtK"]); Assert.Throws<BusinessException>(() => RetrievalQuality.Measure(["duplicate", "duplicate"], ["duplicate"], 5));
    }
    [Fact]
    public void Baseline_comparison_detects_critical_recovery_latency_and_incompatible_data()
    {
        var old = new EvaluationReport("contracts-v1", "1", "hash", "deterministic-fixtures", DateTimeOffset.UtcNow,
            [new("auth", "security", true, true, "verified", 10, [], false), new("recover", "order", false, false, "failed", 10, [], false)], 1, 1, 0, 20);
        var current = old with { Cases = [old.Cases[0] with { Passed = false, DurationMs = 100 }, old.Cases[1] with { Passed = true }] };
        var comparison = EvaluationEngine.Compare(current, old, new()); Assert.Equal(1, comparison.CriticalRegressions); Assert.Contains("recover", comparison.Recovered); Assert.Contains("auth", comparison.LatencyRegressions);
        Assert.False(EvaluationEngine.Compare(current with { DatasetHash = "other" }, old, new()).Comparable);
    }
}
