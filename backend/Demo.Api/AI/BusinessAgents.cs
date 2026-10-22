using System.Security.Claims;
using Demo.Api.Errors;
using Demo.Api.Services;

namespace Demo.Api.AI;

public static class InventoryAgent
{
    public static IReadOnlyList<AiToolDefinition> Tools { get; } = Array.AsReadOnly(AiToolRegistry.All.Where(t => t.Name is
        "get_inventory_overview" or "get_low_stock_products" or "get_out_of_stock_products" or "get_stock_risk").ToArray());
}

public sealed record OrderDraftLine(Guid ProductId, int Quantity);
public sealed record OrderDraftRequest(Guid CustomerId, List<OrderDraftLine> Lines);
public sealed record OrderDraft(Guid CustomerId, IReadOnlyList<OrderDraftLine> Lines, string Status = "DraftOnly",
    bool Persisted = false, bool RequiresHumanReview = true, Guid? ExecutionId = null);

// Preparation only: no OrderService, EF context, model-generated executable action or persistence dependency.
public sealed class OrderDraftAgent(AiToolExecutor authorization, AiExecutionJournal journal)
{
    public async Task<OrderDraft> Prepare(OrderDraftRequest request, ClaimsPrincipal user, string requestId, CancellationToken ct)
    {
        await authorization.Recheck(user, AccessPolicies.BusinessRead, ct);
        if (request.CustomerId == Guid.Empty || request.Lines is null || request.Lines.Count is < 1 or > 20
            || request.Lines.Any(l => l is null || l.ProductId == Guid.Empty || l.Quantity is < 1 or > 10000)
            || request.Lines.Select(l => l.ProductId).Distinct().Count() != request.Lines.Count)
            throw new BusinessException("aiInvalidRequest");
        // IDs are suggestions, not verified references; prices, availability and totals await human review.
        var execution = await journal.Begin(Guid.Parse(user.FindFirstValue("sub")!), null,
            new AiAssistantOptions { Provider = "DraftOnly", Model = "deterministic-preparation" }, requestId, ct);
        await journal.End(execution, "draftPrepared", 0, 0, 0);
        return new(request.CustomerId, request.Lines.ToArray(), ExecutionId: execution);
    }
}

// Provider-neutral compatibility boundaries. Phase 11.5 implementations are registered under AI/Knowledge.
public sealed record RagQuery(string Text, int Limit = 5);
public sealed record RagPassage(string DocumentId, string Text, double Score);
public interface IRagRetriever
{
    // Implementation must enforce current-user document access BEFORE ranking and returning passages.
    Task<IReadOnlyList<RagPassage>> RetrieveAuthorized(RagQuery query, ClaimsPrincipal user, CancellationToken ct);
}
public interface IEmbeddingClient
{
    Task<ReadOnlyMemory<float>> Embed(string text, CancellationToken ct);
}
