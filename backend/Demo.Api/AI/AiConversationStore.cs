using System.Security.Claims;
using System.Text.Json;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI;
public sealed record AiConversationSummary(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, int Turns);
public sealed record AiHistoryMessage(string Role, string Content, string Language, List<AiSource> Sources, DateTimeOffset CreatedAt, bool IsMock, string ResponseKind = "model");
public sealed record AiConversationHistory(Guid Id, DateTimeOffset ExpiresAt, List<AiHistoryMessage> Messages);

public sealed class AiConversationStore(AppDbContext db, AiToolExecutor access, AiAssistantOptions options)
{
    private static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
    public async Task<List<AiConversationSummary>> List(ClaimsPrincipal user, CancellationToken ct)
    {
        await access.Recheck(user, AccessPolicies.BusinessRead, ct);
        return await db.Set<AiConversation>().AsNoTracking().Where(c => c.UserId == Owner(user) && c.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(c => c.CreatedAt).Take(20).Select(c => new AiConversationSummary(c.Id, c.CreatedAt, c.ExpiresAt, c.Revision)).ToListAsync(ct);
    }
    public async Task<AiConversation> Owned(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        await access.Recheck(user, AccessPolicies.BusinessRead, ct);
        return await db.Set<AiConversation>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.UserId == Owner(user) && c.ExpiresAt > DateTimeOffset.UtcNow, ct)
            ?? throw new BusinessException("aiConversationNotFound", 404);
    }
    public async Task<AiConversationHistory> Read(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var conversation = await Owned(id, user, ct);
        var rows = await db.Set<AiConversationMessage>().AsNoTracking().Where(m => m.ConversationId == id).OrderBy(m => m.Sequence).Take(20).ToListAsync(ct);
        await access.Recheck(user, AccessPolicies.BusinessRead, ct);
        return new(id, conversation.ExpiresAt, rows.Select(m => new AiHistoryMessage(m.Role, m.Content, m.Language,
            JsonSerializer.Deserialize<List<AiSource>>(m.SourcesJson, AiAssistantService.Json)!, m.CreatedAt, m.IsMock, m.IsMock?"mock":AiAnswerComposer.Kind(m.Content))).ToList());
    }
    public async Task<AiConversation> Create(ClaimsPrincipal user, CancellationToken ct)
    {
        await access.Recheck(user, AccessPolicies.BusinessRead, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({Owner(user).ToString()}, 110))", ct);
        if (await db.Set<AiConversation>().CountAsync(c => c.UserId == Owner(user) && c.ExpiresAt > DateTimeOffset.UtcNow, ct) >= 20)
            throw new BusinessException("aiConversationLimit", 409);
        var item = new AiConversation { UserId = Owner(user), ExpiresAt = DateTimeOffset.UtcNow.AddDays(options.RetentionDays) };
        db.Add(item); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return item;
    }
    public async Task Delete(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        await Owned(id, user, ct);
        await db.Set<AiConversation>().Where(c => c.Id == id && c.UserId == Owner(user)).ExecuteDeleteAsync(ct);
    }
    public async Task Append(AiConversation conversation, string question, AiChatResponse response, ClaimsPrincipal user, CancellationToken ct)
    {
        await access.Recheck(user, AccessPolicies.BusinessRead, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.Set<AiConversation>().Where(c => c.Id == conversation.Id && c.UserId == Owner(user)
            && c.Revision == conversation.Revision && c.Revision < 10 && c.ExpiresAt > DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, c => c.Revision + 1), ct);
        if (updated != 1) throw new BusinessException("aiConversationConflict", 409);
        db.AddRange(new AiConversationMessage { ConversationId = conversation.Id, Sequence = conversation.Revision * 2, Role = "user", Content = question, Language = response.Language },
            new AiConversationMessage { ConversationId = conversation.Id, Sequence = conversation.Revision * 2 + 1, Role = "assistant", Content = response.Answer,
                Language = response.Language, IsMock = response.IsMock, SourcesJson = JsonSerializer.Serialize(response.Sources, AiAssistantService.Json) });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
}
