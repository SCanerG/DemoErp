using System.Security.Claims;
using System.Text.Json;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI.Monitoring;

public interface IOperationalNotifications
{
    Task Publish(Guid rule, Guid run, MonitoringAnalysis result, MonitoringSettings settings, CancellationToken ct);
}
// In-app only. Future outbound channels require a separately authorized implementation and data policy.
public sealed class OperationalAlerts(AppDbContext db, AiToolExecutor access) : IOperationalNotifications
{
    public IQueryable<OperationalAlert> Visible(Guid user) => db.Set<OperationalAlert>().Where(a => a.OwnerId == null || a.OwnerId == user);
    public async Task<OperationalAlert> Own(Guid id, ClaimsPrincipal user, bool write, CancellationToken ct)
    {
        await access.Recheck(user, write ? AccessPolicies.BusinessWrite : AccessPolicies.BusinessRead, ct);
        var owner = Guid.Parse(user.FindFirstValue("sub")!);
        return await Visible(owner).AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new BusinessException("monitoringNotFound", 404);
    }
    public async Task Change(Guid id, string action, ClaimsPrincipal user, CancellationToken ct)
    {
        if (action is not ("acknowledged" or "resolved" or "dismissed")) throw new BusinessException("monitoringInvalidAction");
        await Own(id, user, true, ct); var actor = Guid.Parse(user.FindFirstValue("sub")!);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var alert = await db.Set<OperationalAlert>().FromSqlInterpolated($"SELECT * FROM \"OperationalAlerts\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
        await access.Recheck(user, AccessPolicies.BusinessWrite, ct);
        if (alert.Status != action && alert.ConditionActive)
        {
            alert.Status = action; History(alert, action, actor); await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
    public void History(OperationalAlert alert, string action, Guid? actor = null)
        => db.Add(new AlertHistory { AlertId = alert.Id, Action = action, ActorId = actor, Identity = actor is null ? MonitoringScope.Identity : "authenticated-user" });
    public async Task Publish(Guid rule, Guid run, MonitoringAnalysis result, MonitoringSettings settings, CancellationToken ct)
    {
        if (result.Findings.Count > 250 || result.Findings.Select(f => f.Key).Distinct().Count() != result.Findings.Count) throw new BusinessException("monitoringInvalidFinding", 503);
        var keys = result.Findings.Select(f => f.Key).ToArray();
        var rows = await db.Set<OperationalAlert>().Where(a => a.RuleId == rule && (a.ConditionActive || keys.Contains(a.Key))).ToListAsync(ct);
        foreach (var finding in result.Findings)
        {
            if (finding.Key.Length > 160 || finding.Label.Length > 160 || finding.Severity is not ("critical" or "warning")) throw new BusinessException("monitoringInvalidFinding", 503);
            var row = rows.SingleOrDefault(a => a.Key == finding.Key);
            if (row is null && finding.Severity == "warning" && !settings.WarningAlerts) continue;
            var facts = JsonSerializer.Serialize(finding.Metrics, AiAssistantService.Json);
            if (facts.Length > 4000) throw new BusinessException("monitoringInvalidFinding", 503);
            if (row is null)
            {
                row = new() { RuleId = rule, Key = finding.Key, Category = finding.Category, EntityId = finding.EntityId, EntityType = finding.EntityType, OwnerId = finding.OwnerId };
                db.Add(row); History(row, "created");
            }
            else if (!row.ConditionActive)
            {
                row.Occurrence++; row.Status = "open"; History(row, "reopened");
            }
            else if (row.Facts != facts || row.Severity != finding.Severity) History(row, "findingsChanged");
            row.ConditionActive = true; row.LastSeenAt = DateTimeOffset.UtcNow; row.RunId = run; row.Facts = facts;
            row.Severity = finding.Severity; row.Label = finding.Label; row.DueAt = finding.DueAt; row.Category = finding.Category;
        }
        // A truncated/partial scan cannot prove that a previously observed condition has cleared.
        if (result.Complete) foreach (var row in rows.Where(a => a.ConditionActive && !keys.Contains(a.Key)))
        {
            row.ConditionActive = false; row.Status = "resolved"; History(row, "conditionCleared");
        }
    }
}
