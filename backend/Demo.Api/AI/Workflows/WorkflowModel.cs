using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI.Workflows;

public sealed class WorkflowOptions
{
    public bool Enabled { get; set; }
    public int PollSeconds { get; set; } = 5;
    public static WorkflowOptions Read(IConfiguration configuration)
    {
        var options = new WorkflowOptions(); configuration.GetSection("Workflows").Bind(options);
        if (options.PollSeconds is < 1 or > 60) throw new InvalidOperationException("Invalid workflow polling interval.");
        return options;
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowPolicy(int TimeoutSeconds = 120, int Retries = 1, int MaxAgents = 3, int MaxTools = 10);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowConfig(decimal Threshold = 10, string Field = "count", string Comparison = "gt", decimal Number = 0,
    string Status = "Pending", DateTimeOffset? Date = null, bool Expected = true, int DayOfWeek = 5, int HourUtc = 9, int MinuteUtc = 0,
    int PeriodDays = 7, bool Summarize = false, bool Optional = true, string Question = "", Guid? CustomerId = null,
    Guid? ProductId = null, int Quantity = 1, int Retries = 1, string Language = "tr");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowNode(string Id, string Type, double X, double Y, WorkflowConfig Config);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowEdge(string Id, string Source, string Target, string When = "always");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowGraph(int SchemaVersion, List<WorkflowNode> Nodes, List<WorkflowEdge> Edges, WorkflowPolicy Policy);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowSave(string Name, string Description, int Revision, WorkflowGraph Graph);
public sealed record WorkflowCommand(int Revision, bool Enabled = false);
public sealed record WorkflowRunRequest(Guid IdempotencyKey, bool Simulation = true);
public sealed record WorkflowValue(decimal Count = 0, decimal? Stock = null, decimal? Sold = null, decimal? Sales = null,
    string? Status = null, DateTimeOffset? DueAt = null, bool Evidence = false, bool Decision = true, bool Approved = false,
    string Kind = "deterministic", JsonElement? Data = null, Guid? AgentExecutionId = null, Guid? ProposalId = null,
    Guid? ProductFilter = null, decimal? WorkflowThreshold = null);
public sealed class WorkflowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Draft { get; set; } = "";
    public int Revision { get; set; } = 1;
    public int? PublishedVersion { get; set; }
    public bool Enabled { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public string? LastSignal { get; set; }
    public int SignalOccurrence { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class WorkflowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public int Number { get; set; }
    public string Graph { get; set; } = "";
    public string OwnerRole { get; set; } = "";
    public int SecurityVersion { get; set; }
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class WorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Guid VersionId { get; set; }
    public Guid RequestKey { get; set; }
    public Guid OwnerId { get; set; }
    public string Mode { get; set; } = "simulation";
    public string State { get; set; } = "queued";
    public string? Error { get; set; }
    public string? Lease { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string TraceId { get; set; } = "";
    public int AgentCalls { get; set; }
    public int ToolCalls { get; set; }
}
public sealed class WorkflowNodeRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string NodeId { get; set; } = "";
    public string Type { get; set; } = "";
    public string State { get; set; } = "running";
    public string Output { get; set; } = "{}";
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
}
public sealed class WorkflowNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid OwnerId { get; set; }
    public string NodeId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Facts { get; set; } = "{}";
    public bool Read { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public static class WorkflowModel
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, NumberHandling=JsonNumberHandling.Strict, MaxDepth = 20 };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Read<T>(string json)
    {
        try { using var document=JsonDocument.Parse(json,new(){MaxDepth=20});Unique(document.RootElement);return document.RootElement.Deserialize<T>(Json) ?? throw new JsonException(); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { throw new BusinessException("workflowInvalidGraph"); }
    }
    private static void Unique(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var property in element.EnumerateObject()){if(!names.Add(property.Name))throw new JsonException();Unique(property.Value);}}
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())Unique(item);
    }
    public static void Configure(ModelBuilder model)
    {
        model.Entity<WorkflowDefinition>(e => { e.ToTable("WorkflowDefinitions"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.OwnerId, x.UpdatedAt }); e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Description).HasMaxLength(500); e.Property(x => x.Draft).HasMaxLength(60000); e.Property(x => x.Revision).IsConcurrencyToken(); e.Property(x => x.LastSignal).HasMaxLength(64); e.HasOne<Domain.User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<WorkflowVersion>(e => { e.ToTable("WorkflowVersions"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.WorkflowId, x.Number }).IsUnique(); e.Property(x => x.Graph).HasMaxLength(60000); e.Property(x => x.OwnerRole).HasMaxLength(20); e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<WorkflowRun>(e => { e.ToTable("WorkflowRuns"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.WorkflowId, x.RequestKey }).IsUnique(); e.HasIndex(x => new { x.State, x.CreatedAt }); e.HasIndex(x => new { x.OwnerId, x.CreatedAt }); e.Property(x => x.State).HasMaxLength(24); e.Property(x => x.Mode).HasMaxLength(20); e.Property(x => x.Error).HasMaxLength(64); e.Property(x => x.TraceId).HasMaxLength(32); e.Property(x => x.Lease).HasMaxLength(32); e.HasOne<WorkflowVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict); e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<WorkflowNodeRun>(e => { e.ToTable("WorkflowNodeRuns"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.RunId, x.NodeId }).IsUnique(); e.Property(x => x.NodeId).HasMaxLength(40); e.Property(x => x.Type).HasMaxLength(40); e.Property(x => x.State).HasMaxLength(24); e.Property(x => x.Output).HasMaxLength(40000); e.Property(x => x.Error).HasMaxLength(64); e.HasOne<WorkflowRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade); });
        model.Entity<WorkflowNotification>(e => { e.ToTable("WorkflowNotifications"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.RunId, x.NodeId }).IsUnique(); e.HasIndex(x => new { x.OwnerId, x.CreatedAt }); e.Property(x => x.NodeId).HasMaxLength(40); e.Property(x => x.Title).HasMaxLength(100); e.Property(x => x.Facts).HasMaxLength(40000); e.HasOne<WorkflowRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade); });
    }
}
