using System.Security.Claims;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Demo.Api.AI.Monitoring;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Controllers;

public sealed record MonitoringTrigger(Guid IdempotencyKey);
public sealed record MonitoringAction(string Action);
public sealed record MonitoringReviewRequest(string Purpose, string Language = "tr");
public sealed record MonitoringReviewResult(Guid ExecutionId, bool Started, AgentResult? Result, string? Error);

[ApiController, Route("api/operations"), Authorize(Policy = AccessPolicies.BusinessRead), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MonitoringController(AppDbContext db, MonitoringOptions options, MonitoringEngine engine, OperationalAlerts alerts,
    AiToolExecutor access, AgentOrchestrator agents, Demo.Api.AI.Knowledge.LocalModelGate inference) : ControllerBase
{
    private Guid Owner => Guid.Parse(User.FindFirstValue("sub")!);
    private async Task Read(CancellationToken ct) => await access.Recheck(User, AccessPolicies.BusinessRead, ct);
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        await Read(ct); var now = DateTimeOffset.UtcNow; var visible = alerts.Visible(Owner);
        return Ok(new { options.Enabled, serviceIdentity = MonitoringScope.Identity, scope = "workspace:reporting-read",
            localInference = new { inference.Concurrency, inference.QueueLimit, inference.Pending },
            criticalAlerts = await visible.CountAsync(a => a.ConditionActive && a.Severity == "critical" && (a.Status == "open" || a.Status == "acknowledged"), ct),
            openAlerts = await visible.CountAsync(a => a.ConditionActive && a.Status == "open", ct),
            activeJobs = await db.Set<MonitoringRun>().CountAsync(r => r.State == "queued" || r.State == "running" || r.State == "retry", ct),
            pendingApprovals = await db.Set<AiOrderProposal>().CountAsync(p => p.UserId == Owner && p.State == "pending" && p.ExpiresAt > now, ct),
            inference = "on_demand_only", salesDeliveryDatesSupported = false, purchaseDeliveryDatesSupported = true });
    }
    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken ct)
    {
        await Read(ct); return Ok((await db.Set<MonitoringRule>().AsNoTracking().OrderBy(r => r.Kind).ToListAsync(ct))
            .Select(r => new { r.Id, r.Kind, r.Version, r.NextRunAt, settings = MonitoringEngine.Settings(r.Settings) }));
    }
    [HttpPut("rules/{id:guid}"), Authorize(Policy = AccessPolicies.UserManage), EnableRateLimiting("monitoring-actions")]
    public async Task<IActionResult> Configure(Guid id, MonitoringConfiguration input, CancellationToken ct)
    {
        await access.Recheck(User, AccessPolicies.UserManage, ct); input.Settings.Validate();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rule = await db.Set<MonitoringRule>().FromSqlInterpolated($"SELECT * FROM \"MonitoringRules\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new BusinessException("monitoringNotFound", 404);
        if (rule.Version != input.Version) throw new BusinessException("monitoringConfigurationChanged", 409);
        rule.Version++; rule.Settings = JsonSerializer.Serialize(input.Settings, AiAssistantService.Json); rule.Enabled = input.Settings.Enabled;
        rule.NextRunAt = DateTimeOffset.UtcNow; rule.LastEventAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }
    [HttpPost("rules/{id:guid}/run"), Authorize(Policy = AccessPolicies.BusinessWrite), EnableRateLimiting("monitoring-actions")]
    public async Task<IActionResult> Run(Guid id, MonitoringTrigger input, CancellationToken ct) => Accepted(new { id = await engine.Enqueue(id, input.IdempotencyKey, User, ct) });
    [HttpGet("runs")]
    public async Task<IActionResult> Runs(CancellationToken ct)
    {
        await Read(ct); var owner = Owner; var admin = User.FindFirstValue("role") == "Admin";
        return Ok(await db.Set<MonitoringRun>().AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(50)
            .Select(r => new { r.Id, r.RuleId, r.Trigger, r.Identity, r.State, r.Attempts, r.CreatedAt, r.StartedAt, r.EndedAt, r.DurationMs, r.Findings, r.Error, r.Diagnostics,
                canCancel = (admin || r.RequestedBy == owner) && (r.State == "queued" || r.State == "running" || r.State == "retry") }).ToListAsync(ct));
    }
    [HttpPost("runs/{id:guid}/cancel"), Authorize(Policy = AccessPolicies.BusinessWrite), EnableRateLimiting("monitoring-actions")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await engine.Cancel(id, User, ct); return NoContent(); }
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] string state = "active", [FromQuery] int page = 1, CancellationToken ct = default)
    {
        await Read(ct); if (state is not ("active" or "all") || page is < 1 or > 10000) throw new BusinessException("monitoringInvalidAction");
        var query = alerts.Visible(Owner).AsNoTracking();
        if (state == "active") query = query.Where(a => a.ConditionActive && (a.Status == "open" || a.Status == "acknowledged"));
        return Ok(new { total = await query.CountAsync(ct), items = await query.OrderByDescending(a => a.Severity == "critical").ThenByDescending(a => a.LastSeenAt).ThenBy(a => a.Id).Skip((page - 1) * 25).Take(25).ToListAsync(ct) });
    }
    [HttpGet("alerts/{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct)
    {
        await alerts.Own(id, User, false, ct); return Ok(await db.Set<AlertHistory>().AsNoTracking().Where(h => h.AlertId == id).OrderByDescending(h => h.CreatedAt).Take(100).ToListAsync(ct));
    }
    [HttpPost("alerts/{id:guid}/state"), Authorize(Policy = AccessPolicies.BusinessWrite), EnableRateLimiting("monitoring-actions")]
    public async Task<IActionResult> State(Guid id, MonitoringAction input, CancellationToken ct) { await alerts.Change(id, input.Action, User, ct); return NoContent(); }

    [HttpPost("alerts/{id:guid}/review"), EnableRateLimiting("monitoring-reviews")]
    public async Task<ActionResult<MonitoringReviewResult>> Review(Guid id, MonitoringReviewRequest input, CancellationToken ct)
    {
        options.Check();
        if (input.Purpose is not ("explain" or "policy" or "purchase") || input.Language is not ("tr" or "en")) throw new BusinessException("monitoringInvalidAction");
        var alert = await alerts.Own(id, User, input.Purpose == "purchase", ct);
        if (alert.Category is not ("outOfStock" or "criticalInventory" or "consumptionChange") || !alert.ConditionActive) throw new BusinessException("monitoringReviewUnavailable", 409);
        Guid execution;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Same owner + occurrence + purpose returns the same execution, even across API instances/retries.
            var key = $"monitor-review:{id}:{Owner}:{alert.Occurrence}:{input.Purpose}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", ct);
            var previous = await db.Set<MonitoringReview>().AsNoTracking().SingleOrDefaultAsync(r => r.AlertId == id && r.UserId == Owner && r.Occurrence == alert.Occurrence && r.Purpose == input.Purpose, ct);
            if (previous is not null) { await tx.CommitAsync(ct); return new MonitoringReviewResult(previous.ExecutionId, false, null, null); }
            var mode = input.Purpose == "purchase" ? "procurement" : input.Purpose == "policy" ? "inventoryknowledge" : "collaboration";
            execution = await agents.Start(new(mode), User, HttpContext.TraceIdentifier, ct);
            db.Add(new MonitoringReview { AlertId = id, UserId = Owner, Occurrence = alert.Occurrence, Purpose = input.Purpose, ExecutionId = execution });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        var message = input.Purpose == "purchase" ? "Analyze sales and inventory and prepare purchase orders for shortages."
            : input.Purpose == "policy" ? "Analyze inventory and explain the documented replenishment policy." : "Analyze inventory and sales for replenishment review.";
        try { return new MonitoringReviewResult(execution, true, await agents.Execute(execution, new(message, input.Language, "last30days"), User, ct), null); }
        catch (BusinessException ex) { return new MonitoringReviewResult(execution, true, null, ex.Code); }
    }
}
