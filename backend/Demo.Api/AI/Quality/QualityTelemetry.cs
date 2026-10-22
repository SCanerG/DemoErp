using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Demo.Api.Data;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Demo.Api.AI.Quality;

// Explicit structured metadata only. Never accept arbitrary tag maps, prompts, arguments or exception messages.
public sealed partial class QualityTelemetry : IDisposable
{
    public const string SourceName = "DemoERP.AI";
    public ActivitySource Source { get; } = new(SourceName, "11.7");
    private readonly ActivityListener listener;
    private readonly Meter meter = new(SourceName, "11.7");
    private readonly Histogram<double> duration;
    private readonly Counter<long> operations;
    private readonly Counter<long> tokenCount;
    private readonly Channel<QualitySpanRow> queue = Channel.CreateBounded<QualitySpanRow>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private readonly IHttpContextAccessor context;
    private readonly QualityOptions options;
    private readonly string environment;
    private readonly SemaphoreSlim flushGate = new(1, 1);
    private long dropped;
    private DateTimeOffset nextPurge = DateTimeOffset.MinValue;
    public long Dropped => Interlocked.Read(ref dropped);
    public QualityTelemetry(IHttpContextAccessor context, QualityOptions options, IHostEnvironment environment)
    {
        this.context = context; this.options = options; this.environment = environment.IsDevelopment() ? "Development" : environment.IsProduction() ? "Production" : "Other";
        duration = meter.CreateHistogram<double>("demoerp.ai.operation.duration", "ms"); operations = meter.CreateCounter<long>("demoerp.ai.operations"); tokenCount = meter.CreateCounter<long>("demoerp.ai.tokens");
        listener = new ActivityListener { ShouldListenTo = source => ReferenceEquals(source, Source), Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded };
        ActivitySource.AddActivityListener(listener);
    }
    public QualityTrace? Begin(string operation, string component, Guid? execution = null, Guid? user = null)
    {
        if (!options.Enabled) return null;
        var owner = user ?? (Guid.TryParse(context.HttpContext?.User.FindFirstValue("sub"), out var id) ? id : (Guid?)null);
        if (owner is null && Activity.Current?.Source == Source && Guid.TryParse(Activity.Current.GetTagItem("demo.owner_id")?.ToString(), out var inheritedOwner)) owner = inheritedOwner;
        var name = operation is "request" or "action" or "orchestration" or "agent" or "tool" or "retrieval" or "provider" or "embedding" or "approval" or "order" or "monitoring" or "evaluation" or "workflow" or "workflowNode" ? operation : "other";
        var safeComponent = Safe(component, 64);
        var activity = Source.StartActivity(name, ActivityKind.Internal)!;
        // A private execution identifier may be inherited internally; user-supplied baggage is never persisted.
        var inherited = activity.Parent?.Source == Source ? activity.Parent.GetTagItem("demo.execution_id")?.ToString() : null;
        if (execution is null && Guid.TryParse(inherited, out var parentExecution)) execution = parentExecution;
        activity.SetTag("demo.operation", name); activity.SetTag("demo.component", safeComponent); activity.SetTag("demo.execution_id", execution?.ToString()); activity.SetTag("demo.owner_id", owner?.ToString());
        return new(this, activity, new QualitySpanRow { UserId = owner, ExecutionId = execution, Operation = name, Component = safeComponent, Environment = environment, StartedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(options.RetentionDays) });
    }
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._:/-]*$", RegexOptions.CultureInvariant)] private static partial Regex Token();
    public static string Safe(string? value, int max = 64) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && Token().IsMatch(value) && !AiOutputValidator.ContainsSecret(value, "") ? value : "redacted";
    internal void Complete(QualityTrace trace)
    {
        var row = trace.Row;
        row.TraceId = trace.Activity.TraceId.ToHexString(); row.SpanId = trace.Activity.SpanId.ToHexString(); row.ParentSpanId = trace.Activity.ParentSpanId.ToHexString();
        trace.Activity.SetTag("demo.outcome", row.Outcome); trace.Activity.SetTag("error.type", row.Error); trace.Activity.SetTag("gen_ai.provider.name", row.Provider); trace.Activity.SetTag("gen_ai.request.model", row.Model);
        trace.Activity.SetTag("demo.tokens", row.Tokens); trace.Activity.SetTag("demo.context_characters", row.ContextCharacters); trace.Activity.SetTag("demo.retry", row.Retry); trace.Activity.SetTag("demo.retrieved", row.RetrievedCount);
        trace.Activity.Stop(); row.DurationMs = Math.Max(0, (long)trace.Activity.Duration.TotalMilliseconds);
        var tags = new TagList { { "operation", row.Operation }, { "outcome", row.Outcome }, { "provider", row.Provider ?? "none" } };
        duration.Record(row.DurationMs, tags); operations.Add(1, tags); if (row.Tokens is int tokens) tokenCount.Add(tokens, tags);
        if (!queue.Writer.TryWrite(row)) Interlocked.Increment(ref dropped);
    }
    public async Task Flush(IServiceScopeFactory scopes, CancellationToken ct)
    {
        await flushGate.WaitAsync(ct);
        try
        {
            var batch = new List<QualitySpanRow>(); while (batch.Count < 128 && queue.Reader.TryRead(out var row)) batch.Add(row);
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (batch.Count > 0)
            {
                try {
                    db.AddRange(batch); await db.SaveChangesAsync(ct);
                    var overflow = await db.Set<QualitySpanRow>().OrderByDescending(s => s.StartedAt).Skip(options.MaxSpans).Select(s => s.Id).Take(128).ToArrayAsync(ct);
                    if(overflow.Length>0)await db.Set<QualitySpanRow>().Where(s=>overflow.Contains(s.Id)).ExecuteDeleteAsync(ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested) { Interlocked.Add(ref dropped, batch.Count); db.ChangeTracker.Clear(); }
            }
            if (nextPurge <= DateTimeOffset.UtcNow)
            {
                await Purge(db, options, DateTimeOffset.UtcNow, ct); nextPurge = DateTimeOffset.UtcNow.AddMinutes(1);
            }
        }
        finally { flushGate.Release(); }
    }
    public static async Task Purge(AppDbContext db, QualityOptions options, DateTimeOffset now, CancellationToken ct)
    {
        await db.Set<QualitySpanRow>().Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        await db.Set<QualityEvaluationRun>().Where(r => r.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        var extra = await db.Set<QualitySpanRow>().OrderByDescending(s => s.StartedAt).Skip(options.MaxSpans).Select(s => s.Id).Take(5000).ToArrayAsync(ct);
        if (extra.Length > 0) await db.Set<QualitySpanRow>().Where(s => extra.Contains(s.Id)).ExecuteDeleteAsync(ct);
        var runs = await db.Set<QualityEvaluationRun>().OrderByDescending(r => r.CreatedAt).Skip(options.MaxEvaluationRuns).Select(r => r.Id).Take(1000).ToArrayAsync(ct);
        if (runs.Length > 0) await db.Set<QualityEvaluationRun>().Where(r => runs.Contains(r.Id)).ExecuteDeleteAsync(ct);
    }
    public void Dispose() { listener.Dispose(); Source.Dispose(); meter.Dispose(); flushGate.Dispose(); }
}
public sealed class QualityTrace(QualityTelemetry telemetry, Activity activity, QualitySpanRow row) : IDisposable
{
    internal Activity Activity { get; } = activity;
    internal QualitySpanRow Row { get; } = row;
    private int disposed;
    public void Provider(string provider, string model) { Row.Provider = provider is "Ollama" or "OpenAI" or "Mock" ? provider : "Other"; Row.Model = QualityTelemetry.Safe(model, 100); }
    public void Context(int characters) => Row.ContextCharacters = Math.Clamp(characters, 0, 1000000);
    public void Retrieved(int count) => Row.RetrievedCount = Math.Clamp(count, 0, 1000);
    public void Usage(int? tokens) { Row.Tokens = tokens is >= 0 and <= 1000000 ? tokens : null; }
    public void Retry(int retry) => Row.Retry = Math.Clamp(retry, 0, 10);
    public void Execution(Guid id) { Row.ExecutionId = id; Activity.SetTag("demo.execution_id", id.ToString()); }
    public void Owner(Guid id) { Row.UserId=id;Activity.SetTag("demo.owner_id",id.ToString()); }
    public void Outcome(string outcome) { Row.Outcome = outcome is "model" or "model_extract" or "deterministic" ? "success" : outcome is "success" or "fallback" or "mock" or "insufficient" or "cancelled" or "timeout" or "retry" ? outcome : "failure"; }
    public void Fail(Exception error) {
        Row.Error = error is BusinessException b ? QualityTelemetry.Safe(b.Code) : error is OperationCanceledException ? "cancelled" : "executionFailure";
        Row.Outcome = error is OperationCanceledException || Row.Error is "aiCancelled" or "knowledgeCancelled" or "monitoringCancelled" ? "cancelled"
            : Row.Error is "aiTimeout" or "knowledgeTimeout" or "monitoringTimeout" ? "timeout" : "failure";
        Activity.SetStatus(ActivityStatusCode.Error);
    }
    public void Status(string status) { if (status is "success" or "fallback" or "mock" or "insufficient" or "cancelled" or "timeout" or "retry") Outcome(status); else Fail(new BusinessException(status)); }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) telemetry.Complete(this); }
}
public sealed class QualityRequestFilter(QualityTelemetry telemetry, AiAssistantOptions options) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path;
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method) || !(path.StartsWithSegments("/api/ai") || path == "/api/knowledge/chat" || path.StartsWithSegments("/api/operations") || path.StartsWithSegments("/api/workflows"))) { await next(); return; }
        // Controller/action names are static source metadata, never route values or user-supplied paths.
        context.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller); context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
        var component = controller + "." + action;
        using var trace = telemetry.Begin("action", component);
        trace?.Provider(options.Provider, options.Model);
        context.HttpContext.Response.Headers["X-AI-Trace-Id"] = Activity.Current?.TraceId.ToHexString();
        try
        {
            var result = await next();
            if (result.Exception is not null) trace?.Fail(result.Exception);
            else if (result.Result is Microsoft.AspNetCore.Mvc.ObjectResult objectResult)
            {
                if (objectResult.StatusCode >= 400) trace?.Outcome("failure");
                switch (objectResult.Value)
                {
                    case Agents.AgentResult agent: trace?.Execution(agent.ExecutionId); trace?.Outcome(agent.ResponseKind); context.HttpContext.Items["DemoERP.Outcome"]=agent.ResponseKind;context.HttpContext.Items["DemoERP.Execution"]=agent.ExecutionId;break;
                    case AiChatResponse chat: if (chat.ExecutionId is Guid execution) {trace?.Execution(execution);context.HttpContext.Items["DemoERP.Execution"]=execution;} trace?.Outcome(chat.ResponseKind);context.HttpContext.Items["DemoERP.Outcome"]=chat.ResponseKind;break;
                    case Knowledge.KnowledgeAnswer answer: var outcome=answer.InsufficientEvidence ? "insufficient" : "success";trace?.Outcome(outcome);context.HttpContext.Items["DemoERP.Outcome"]=outcome;break;
                    case Agents.ProposalView proposal: context.HttpContext.Items["DemoERP.Execution"]=proposal.ExecutionId;break;
                }
            }
        }
        catch (Exception error) { trace?.Fail(error); throw; }
    }
}
public sealed class QualityHttpMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,QualityTelemetry telemetry,AiAssistantOptions options)
    {
        var path=context.Request.Path;
        if(!HttpMethods.IsPost(context.Request.Method)||!(path.StartsWithSegments("/api/ai")||path=="/api/knowledge/chat"||path.StartsWithSegments("/api/operations") || path.StartsWithSegments("/api/workflows"))){await next(context);return;}
        using var trace=telemetry.Begin("request",path.StartsWithSegments("/api/operations")?"monitoring-http":path.StartsWithSegments("/api/knowledge")?"knowledge-http":"ai-http");
        trace?.Provider(options.Provider,options.Model);var traceId=Activity.Current?.TraceId.ToHexString();
        context.Response.OnStarting(()=>{if(traceId is not null)context.Response.Headers["X-AI-Trace-Id"]=traceId;return Task.CompletedTask;});
        var failed=false;
        try {await next(context);}
        catch(Exception error){failed=true;trace?.Fail(error);throw;}
        finally {
            if(Guid.TryParse(context.User.FindFirstValue("sub"),out var owner))trace?.Owner(owner);
            if(context.Items["DemoERP.Execution"] is Guid execution)trace?.Execution(execution);
            if(!failed){if(context.Response.StatusCode>=400)trace?.Fail(new BusinessException(context.Response.StatusCode==401?"aiUnauthorized":context.Response.StatusCode==403?"aiForbidden":"aiRequestFailed",context.Response.StatusCode));else if(context.Items["DemoERP.Outcome"] is string outcome)trace?.Outcome(outcome);}
        }
    }
}
public sealed class QualityTelemetryWorker(QualityTelemetry telemetry, IServiceScopeFactory scopes, QualityOptions options, ILogger<QualityTelemetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await telemetry.Flush(scopes, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("AI quality metadata unavailable; ERP operations are unaffected"); }
            try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
