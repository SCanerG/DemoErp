using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Demo.Api.AI.Monitoring;

public sealed class MonitoringEngine(AppDbContext db, MonitoringOptions options, IMonitoringAnalysis analyzer,
    IOperationalNotifications notifications, AiToolExecutor access, ILogger<MonitoringEngine> logger, Quality.QualityTelemetry? telemetry=null)
{
    public static MonitoringSettings Settings(string json) => JsonSerializer.Deserialize<MonitoringSettings>(json, AiAssistantService.Json)!;
    public async Task<Guid> Enqueue(Guid ruleId, Guid key, ClaimsPrincipal? user, CancellationToken ct, string trigger = "manual")
    {
        options.Check();
        if (key == Guid.Empty) throw new BusinessException("monitoringInvalidAction");
        if (user is not null) await access.Recheck(user, AccessPolicies.BusinessWrite, ct);
        else if (trigger is not ("scheduled" or "event")) throw new BusinessException("monitoringScopeRejected", 403);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rule = await db.Set<MonitoringRule>().FromSqlInterpolated($"SELECT * FROM \"MonitoringRules\" WHERE \"Id\"={ruleId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new BusinessException("monitoringNotFound", 404);
        var owner = user is null ? (Guid?)null : Guid.Parse(user.FindFirstValue("sub")!);
        var duplicate = await db.Set<MonitoringRun>().AsNoTracking().SingleOrDefaultAsync(r => r.RuleId == ruleId && r.RequestKey == key, ct);
        if (duplicate is not null)
        {
            if (duplicate.RequestedBy != owner) throw new BusinessException("monitoringBusy", 409);
            await tx.CommitAsync(ct); return duplicate.Id;
        }
        if (await db.Set<MonitoringRun>().AnyAsync(r => r.RuleId == ruleId && (r.State == "queued" || r.State == "running" || r.State == "retry"), ct)) throw new BusinessException("monitoringBusy", 409);
        var now = DateTimeOffset.UtcNow; var settings = Settings(rule.Settings);
        if (user is null)
        {
            if (!rule.Enabled) throw new BusinessException("monitoringDisabled", 409);
            if (trigger == "scheduled" && rule.NextRunAt > now || trigger == "event" && rule.NextEventAt > now) throw new BusinessException("monitoringBusy", 409);
        }
        var run = new MonitoringRun { RuleId = ruleId, RequestKey = key, RequestedBy = owner, Trigger = trigger, Settings = rule.Settings };
        db.Add(run); rule.NextRunAt = now.AddMinutes(settings.IntervalMinutes); rule.LastEventAt = now; rule.NextEventAt = now.AddMinutes(1);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return run.Id;
    }
    public async Task Schedule(CancellationToken ct)
    {
        if (!options.Enabled) return;
        foreach (var rule in await db.Set<MonitoringRule>().AsNoTracking().Where(r => r.Enabled).ToListAsync(ct))
        {
            var now = DateTimeOffset.UtcNow; var settings = Settings(rule.Settings); var trigger = rule.NextRunAt <= now ? "scheduled" : "";
            if (trigger == "" && settings.EventsEnabled && rule.NextEventAt <= now)
            {
                var entities = rule.Kind == "inventory" ? new[] { "WarehouseInventory", "Order", "StockTransfer", "ProductionOrder", "GoodsReceipt" }
                    : rule.Kind == "orders" ? new[] { "Order", "PurchaseOrder", "AiOrderProposal" } : new[] { "Order" };
                if (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.CreatedAt > rule.LastEventAt && a.CreatedAt <= now && entities.Contains(a.EntityName), ct)) trigger = "event";
            }
            if (trigger == "") continue;
            try { await Enqueue(rule.Id, Guid.NewGuid(), null, ct, trigger); }
            catch (BusinessException ex) when (ex.Code is "monitoringBusy" or "monitoringDisabled") { }
            db.ChangeTracker.Clear();
        }
    }
    public async Task Cancel(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        await access.Recheck(user, AccessPolicies.BusinessWrite, ct);
        var owner = Guid.Parse(user.FindFirstValue("sub")!); var admin = user.FindFirstValue("role") == "Admin";
        var row = await db.Set<MonitoringRun>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && (admin || r.RequestedBy == owner), ct)
            ?? throw new BusinessException("monitoringNotFound", 404);
        await db.Set<MonitoringRun>().Where(r => r.Id == row.Id && (r.State == "queued" || r.State == "running" || r.State == "retry"))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "cancelled").SetProperty(r => r.EndedAt, DateTimeOffset.UtcNow).SetProperty(r => r.Error, "monitoringCancelled"), ct);
        db.Add(new Domain.AuditLog { UserId = owner, UserName = "Authenticated monitor operator", Action = "MonitoringCancel", EntityName = "MonitoringRun", EntityId = id, Description = "Monitoring cancellation requested" });
        await db.SaveChangesAsync(ct);
    }
    public async Task<bool> ProcessNext(CancellationToken ct)
    {
        if (!options.Enabled) return false;
        var now = DateTimeOffset.UtcNow; var stale = now.AddMinutes(-2);
        // Only the current lease may publish. Expired owners cannot commit after another instance reclaims work.
        await db.Set<MonitoringRun>().Where(r => r.State == "running" && r.StartedAt < stale)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "retry").SetProperty(r => r.AvailableAt, now).SetProperty(r => r.Lease, (string?)null).SetProperty(r => r.Error, "monitoringInterrupted"), ct);
        MonitoringRun? run;
        await using (var claim = await db.Database.BeginTransactionAsync(ct))
        {
            run = await db.Set<MonitoringRun>().FromSqlInterpolated($"SELECT * FROM \"MonitoringRuns\" WHERE \"State\" IN ('queued','retry') AND \"AvailableAt\"<={now} ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
            if (run is null) return false;
            run.State = "running"; run.Lease = Guid.NewGuid().ToString("N"); run.StartedAt = now; run.Attempts++;
            await db.SaveChangesAsync(ct); await claim.CommitAsync(ct);
        }
        var id = run.Id; var lease = run.Lease; var watch = Stopwatch.StartNew();
        using var trace=telemetry?.Begin("monitoring","report-only",id);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            if (run.Attempts > 3) throw new BusinessException("monitoringInterrupted", 503);
            var rule = await db.Set<MonitoringRule>().AsNoTracking().SingleAsync(r => r.Id == run.RuleId, deadline.Token);
            var result = await analyzer.Analyze(rule.Kind, Settings(run.Settings), MonitoringScope.Reports, now, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested(); db.ChangeTracker.Clear();
            await using var publish = await db.Database.BeginTransactionAsync(deadline.Token);
            var current = await db.Set<MonitoringRun>().FromSqlInterpolated($"SELECT * FROM \"MonitoringRuns\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(deadline.Token);
            if (current.State != "running" || current.Lease != lease) return true;
            await notifications.Publish(rule.Id, id, result, Settings(run.Settings), deadline.Token);
            current.State = "success"; current.Findings = result.Findings.Count(f => f.OwnerId is null); current.Diagnostics = JsonSerializer.Serialize(result.Diagnostics, AiAssistantService.Json);
            current.EndedAt = DateTimeOffset.UtcNow; current.DurationMs = watch.ElapsedMilliseconds; current.Error = null;
            trace?.Status("success");
            await db.SaveChangesAsync(deadline.Token); await publish.CommitAsync(deadline.Token);
            logger.LogInformation("Monitoring run {RunId} identity {Identity} rule {Kind} outcome success findings {Count} durationMs {Duration}", id, MonitoringScope.Identity, rule.Kind, current.Findings, watch.ElapsedMilliseconds);
        }
        catch (Exception error)
        {
            trace?.Fail(error);
            db.ChangeTracker.Clear(); using var final = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var category = error is OperationCanceledException ? ct.IsCancellationRequested ? "monitoringInterrupted" : "monitoringTimeout" : error is BusinessException b ? b.Code : "monitoringExecutionFailed";
            var transient = error is NpgsqlException { IsTransient: true } || error is TimeoutException || error is OperationCanceledException;
            await using var fail = await db.Database.BeginTransactionAsync(final.Token);
            var current = await db.Set<MonitoringRun>().FromSqlInterpolated($"SELECT * FROM \"MonitoringRuns\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(final.Token);
            if (current.State != "running" || current.Lease != lease) return true;
            current.Error = category; current.DurationMs = watch.ElapsedMilliseconds;
            current.State = transient && current.Attempts < 3 ? "retry" : "failed";
            current.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, current.Attempts) * 2));
            if (current.State == "failed")
            {
                current.EndedAt = DateTimeOffset.UtcNow;
                await notifications.Publish(current.RuleId, id, new([new("execution:failure", "agentExecutionFailure", "critical", "monitor", null, "", new())], false, []), Settings(current.Settings), final.Token);
            }
            await db.SaveChangesAsync(final.Token); await fail.CommitAsync(final.Token);
            logger.LogWarning("Monitoring run {RunId} identity {Identity} outcome {Outcome} errorCategory {Category} durationMs {Duration}", id, MonitoringScope.Identity, current.State, category, watch.ElapsedMilliseconds);
        }
        return true;
    }
}

public sealed class MonitoringWorker(IServiceScopeFactory scopes, MonitoringOptions options, ILogger<MonitoringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var engine = scope.ServiceProvider.GetRequiredService<MonitoringEngine>();
                await engine.Schedule(stoppingToken); await engine.ProcessNext(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Monitoring worker temporarily unavailable; retry after bounded polling interval"); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
