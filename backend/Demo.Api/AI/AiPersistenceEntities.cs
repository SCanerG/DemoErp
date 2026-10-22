namespace Demo.Api.AI;

public sealed class AiConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public int Revision { get; set; }
}
public sealed class AiConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public int Sequence { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string Language { get; set; } = "en";
    public bool IsMock { get; set; }
    public string SourcesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class AiExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ConversationId { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string Outcome { get; set; } = "running";
    public int ProviderCalls { get; set; }
    public int TotalTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? CostCurrency { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class AiToolAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExecutionId { get; set; }
    public string ToolName { get; set; } = "unregistered";
    public string Outcome { get; set; } = "requested";
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
