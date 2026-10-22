using System.Security.Claims;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.AI.Quality;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Controllers;

public sealed record EvaluationRequest(Guid IdempotencyKey);
[ApiController, Route("api/quality"), Authorize(Policy = AccessPolicies.BusinessRead), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QualityController(AppDbContext db, QualityOptions options, QualityTelemetry telemetry, EvaluationCatalog catalog,
    EvaluationEngine engine, AiToolExecutor access) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);
    private async Task<IQueryable<QualitySpanRow>> Visible(string scope, CancellationToken ct)
    {
        await access.Recheck(User, scope == "system" ? AccessPolicies.AuditRead : AccessPolicies.BusinessRead, ct);
        if (scope is not ("mine" or "system")) throw new BusinessException("qualityInvalidRequest");
        var now = DateTimeOffset.UtcNow; var owner = UserId;
        // Admin can inspect system-only jobs, not another person's private request/approval traces.
        return db.Set<QualitySpanRow>().AsNoTracking().Where(s => s.ExpiresAt > now && (scope == "system" ? s.UserId == null : s.UserId == owner));
    }
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct, string scope = "mine")
    {
        var query = await Visible(scope, ct); var from = DateTimeOffset.UtcNow.AddHours(-24);
        var rows = await query.Where(s => s.StartedAt >= from).OrderByDescending(s => s.StartedAt).Take(5001).ToListAsync(ct);
        return Ok(new { options.Enabled, scope, from, until = DateTimeOffset.UtcNow, truncated = rows.Count > 5000, evidence = "persisted-runtime-observations", retentionDays = options.RetentionDays,
            processDroppedObservations = User.FindFirstValue("role") == "Admin" ? (long?)telemetry.Dropped : null, metrics = QualityMetrics.Summarize(rows.Take(5000).ToList()) });
    }
    [HttpGet("traces")]
    public async Task<IActionResult> Traces(CancellationToken ct, string scope = "mine")
    {
        var query = await Visible(scope, ct);
        return Ok(await query.OrderByDescending(s => s.StartedAt).Take(1000).GroupBy(s => s.TraceId).Select(g => new { traceId = g.Key, startedAt = g.Min(s => s.StartedAt), observations = g.Count() }).OrderByDescending(t => t.startedAt).Take(30).ToListAsync(ct));
    }
    [HttpGet("traces/{traceId}")]
    public async Task<IActionResult> Trace(string traceId, CancellationToken ct, string scope = "mine")
    {
        if (traceId.Length != 32 || traceId.Any(c => !Uri.IsHexDigit(c))) throw new BusinessException("qualityInvalidRequest");
        var query = await Visible(scope, ct); var rows = await query.Where(s => s.TraceId == traceId).OrderBy(s => s.StartedAt).Take(250).ToListAsync(ct);
        if (rows.Count == 0) throw new BusinessException("qualityNotFound", 404); return Ok(rows);
    }
    [HttpGet("suites")]
    public async Task<IActionResult> Suites(CancellationToken ct)
    {
        await access.Recheck(User, AccessPolicies.BusinessRead, ct);
        return Ok(new[] { new { catalog.Dataset.Suite, catalog.Dataset.Version, datasetHash = catalog.Hash, mode = "deterministic-fixtures", scenarios = catalog.Dataset.Scenarios.Count,
            categories = catalog.Dataset.Scenarios.GroupBy(s => s.Category).Select(g => new { category = g.Key, scenarios = g.Count() }), liveInference = false, noBusinessWrites = true } });
    }
    [HttpGet("evaluations")]
    public async Task<IActionResult> Evaluations(CancellationToken ct)
    {
        await access.Recheck(User, AccessPolicies.BusinessRead, ct); var owner = UserId; var now = DateTimeOffset.UtcNow;
        return Ok(await db.Set<QualityEvaluationRun>().AsNoTracking().Where(r => r.UserId == owner && r.ExpiresAt > now).OrderByDescending(r => r.CreatedAt).Take(20)
            .Select(r => new { r.Id, r.Suite, r.DatasetHash, r.Mode, r.Status, r.CreatedAt }).ToListAsync(ct));
    }
    private async Task<QualityEvaluationRun> Own(Guid id, CancellationToken ct)
    {
        await access.Recheck(User, AccessPolicies.BusinessRead, ct); var owner = UserId;
        return await db.Set<QualityEvaluationRun>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.UserId == owner && r.ExpiresAt > DateTimeOffset.UtcNow, ct) ?? throw new BusinessException("qualityNotFound", 404);
    }
    [HttpGet("evaluations/{id:guid}")]
    public async Task<IActionResult> Evaluation(Guid id, CancellationToken ct)
    {
        var row = await Own(id, ct); return Ok(new { row.Id, report = JsonSerializer.Deserialize<EvaluationReport>(row.Report, AiAssistantService.Json) });
    }
    [HttpGet("evaluations/{id:guid}/comparison/{baseline:guid}")]
    public async Task<IActionResult> Compare(Guid id, Guid baseline, CancellationToken ct, double latencyRatio = 2, double minimumLatencyIncreaseMs = 20)
    {
        var current = await Own(id, ct); var previous = await Own(baseline, ct);
        return Ok(EvaluationEngine.Compare(JsonSerializer.Deserialize<EvaluationReport>(current.Report, AiAssistantService.Json)!, JsonSerializer.Deserialize<EvaluationReport>(previous.Report, AiAssistantService.Json)!, new(latencyRatio, minimumLatencyIncreaseMs)));
    }
    [HttpPost("evaluations"), Authorize(Policy = AccessPolicies.UserManage), EnableRateLimiting("quality-evaluation"), RequestSizeLimit(1000)]
    public async Task<IActionResult> Run(EvaluationRequest input, CancellationToken ct)
    {
        if (!options.Enabled) throw new BusinessException("qualityDisabled", 503);
        await access.Recheck(User, AccessPolicies.UserManage, ct); if (input.IdempotencyKey == Guid.Empty) throw new BusinessException("qualityInvalidRequest");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var token = deadline.Token; var owner = UserId;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var key = "quality-eval:" + owner; await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", token);
        var existing = await db.Set<QualityEvaluationRun>().AsNoTracking().SingleOrDefaultAsync(r => r.UserId == owner && r.RequestKey == input.IdempotencyKey, token);
        if (existing is not null) { await tx.CommitAsync(token); return Ok(new { existing.Id, existing.Status, reused = true }); }
        if (await db.Set<QualityEvaluationRun>().CountAsync(r => r.UserId == owner && r.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1), token) >= 3) throw new BusinessException("qualityBusy", 429);
        var report = await engine.Run(token); await access.Recheck(User, AccessPolicies.UserManage, token);
        var row = new QualityEvaluationRun { UserId = owner, RequestKey = input.IdempotencyKey, Suite = report.Suite, DatasetHash = report.DatasetHash, Status = report.Failed == 0 ? "success" : "failed", Report = JsonSerializer.Serialize(report, AiAssistantService.Json), ExpiresAt = DateTimeOffset.UtcNow.AddDays(options.RetentionDays) };
        db.Add(row); await db.SaveChangesAsync(token); await tx.CommitAsync(token); return Accepted(new { row.Id, row.Status, reused = false });
    }
}
