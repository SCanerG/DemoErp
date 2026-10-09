using System.Text.Json;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.Controllers;

[ApiController, Authorize(Policy = AccessPolicies.AuditRead), Route("api/audit-logs")]
public sealed class AuditLogsController(AppDbContext db) : ControllerBase
{
    private static readonly string[] Actions = ["Create", "Update", "Delete", "Activate", "Deactivate", "RoleChange", "OrderConfirm", "OrderComplete", "OrderCancel", "StockIn", "StockOut", "StockAdjustment", "MinimumLevelChange"];
    private static readonly string[] Entities = ["Product", "Category", "Customer", "Order", "Inventory", "User"];
    private static JsonElement? Json(string? value) => value is null ? null : JsonSerializer.Deserialize<JsonElement>(value);
    private static AuditResponse Map(AuditLog a) => new(a.Id, a.UserId, a.UserName, a.Action, a.EntityName, a.EntityId, Json(a.OldValues), Json(a.NewValues), a.Description, a.CreatedAt, a.CorrelationId);
    [HttpGet]
    public async Task<ActionResult<AuditPage>> List(CancellationToken ct, int page = 1, int pageSize = 20, Guid? userId = null,
        string? action = null, string? entityName = null, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100 || from > to
            || action is not null && !Actions.Contains(action) || entityName is not null && !Entities.Contains(entityName))
            return Problem(statusCode: 400, title: "Invalid audit filters");
        var query = db.AuditLogs.AsNoTracking();
        if (userId.HasValue) query = query.Where(a => a.UserId == userId);
        if (action is not null) query = query.Where(a => a.Action == action);
        if (entityName is not null) query = query.Where(a => a.EntityName == entityName);
        if (from.HasValue) { var utc = from.Value.ToUniversalTime(); query = query.Where(a => a.CreatedAt >= utc); }
        if (to.HasValue) { var utc = to.Value.ToUniversalTime(); query = query.Where(a => a.CreatedAt <= utc); }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new AuditPage(rows.Select(Map).ToList(), page, pageSize, count));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditResponse>> Get(Guid id, CancellationToken ct) =>
        await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) is { } row ? Ok(Map(row)) : Problem(statusCode: 404, title: "Record not found");
}
