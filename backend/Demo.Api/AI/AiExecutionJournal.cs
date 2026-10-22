using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI;
// Separate contexts ensure cancelled/failed requests can still finalize metadata, without persisting failed message entities.
public sealed class AiExecutionJournal(IServiceScopeFactory scopes, ILogger<AiExecutionJournal> logger, AiToolExecutor access)
{
    public async Task<AiExecutionStatus> Read(Guid id, ClaimsPrincipal principal, CancellationToken ct)
    {
        await access.Recheck(principal, AccessPolicies.BusinessRead, ct);
        var user = Guid.Parse(principal.FindFirstValue("sub")!);
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.Set<AiExecution>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.UserId == user && r.ExpiresAt > DateTimeOffset.UtcNow, ct)
            ?? throw new BusinessException("aiExecutionNotFound", 404);
        var rows = await db.Set<AiToolAudit>().AsNoTracking().Where(t => t.ExecutionId == id).OrderBy(t => t.CreatedAt).Take(10).ToListAsync(ct);
        var tools = rows.Select(t => new AiToolStatus(t.ToolName,t.Outcome,t.DurationMs,t.CreatedAt,t.Outcome is "running" or "requested"?null:t.CreatedAt.AddMilliseconds(t.DurationMs))).ToList();
        await access.Recheck(principal, AccessPolicies.BusinessRead, ct);
        return new(run.Id, run.CorrelationId, run.Provider, run.Model, run.Outcome, run.ProviderCalls, run.TotalTokens, run.DurationMs, run.CreatedAt, tools);
    }
    public async Task<Guid> Begin(Guid user, Guid? conversation, AiAssistantOptions options, string correlationId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = new AiExecution { UserId = user, ConversationId = conversation, Provider = options.Provider, Model = options.Model, CorrelationId = correlationId[..Math.Min(correlationId.Length, 128)],
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(options.RetentionDays) };
        db.Add(run); await db.SaveChangesAsync(ct); if(System.Diagnostics.Activity.Current?.Source.Name==Quality.QualityTelemetry.SourceName)System.Diagnostics.Activity.Current.SetTag("demo.execution_id",run.Id.ToString()); return run.Id;
    }
    public async Task<Guid> ToolBegin(Guid run, string name, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tool = new AiToolAudit { ExecutionId = run, ToolName = (AiToolRegistry.All.Any(t => t.Name == name) || Agents.AgentTools.Known(name)) ? name : "unregistered" };
        db.Add(tool); await db.SaveChangesAsync(ct); return tool.Id;
    }
    public async Task ToolEnd(Guid id, string outcome, long duration)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var metadata=await db.Set<AiToolAudit>().AsNoTracking().Where(t=>t.Id==id).Select(t=>new {t.ExecutionId,t.ToolName}).SingleOrDefaultAsync(deadline.Token);
        if(metadata is not null)logger.LogInformation("AI tool execution {ExecutionId} tool {ToolName} status {Outcome} latencyMs {LatencyMs}",metadata.ExecutionId,metadata.ToolName,outcome,duration);
        await db.Set<AiToolAudit>().Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Outcome, outcome).SetProperty(t => t.DurationMs, duration), deadline.Token);
    }
    public async Task End(Guid id, string outcome, long duration, int calls, int tokens)
    {
        try {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await db.Set<AiExecution>().Where(r => r.Id == id && r.Outcome != "interrupted").ExecuteUpdateAsync(s => s.SetProperty(r => r.Outcome, outcome)
                .SetProperty(r => r.DurationMs, duration).SetProperty(r => r.ProviderCalls, calls).SetProperty(r => r.TotalTokens, tokens), deadline.Token);
        }
        catch (Exception) { logger.LogWarning("AI execution metadata finalization failed for {ExecutionId}", id); }
    }
}

public sealed class AiRetentionWorker(IServiceScopeFactory scopes, ILogger<AiRetentionWorker> logger) : BackgroundService
{
    public static async Task Purge(AppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        await db.Set<AiExecution>().Where(r => r.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        await db.Set<AiConversation>().Where(c => c.ExpiresAt <= now).ExecuteDeleteAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do {
            try {
                await using var scope = scopes.CreateAsyncScope();
                await Purge(scope.ServiceProvider.GetRequiredService<AppDbContext>(), DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("AI retention cleanup failed; expired content remains inaccessible"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
