using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI.Monitoring;

public sealed class MonitoringOptions
{
    public bool Enabled { get; set; }
    public int PollSeconds { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 45;
    public static MonitoringOptions Read(IConfiguration config)
    {
        var result = new MonitoringOptions(); config.GetSection("Monitoring").Bind(result);
        if (result.PollSeconds is < 1 or > 60 || result.TimeoutSeconds is < 5 or > 90) throw new InvalidOperationException("Invalid monitoring limits.");
        return result;
    }
    public void Check() { if (!Enabled) throw new BusinessException("monitoringDisabled", 503); }
}

public sealed record MonitoringSettings(bool Enabled = false, bool EventsEnabled = false, int IntervalMinutes = 60,
    int PeriodDays = 30, int MinimumSamples = 5, decimal PercentageChange = 50, decimal AbsoluteChange = 100,
    int StalledDays = 7, int ApproachingDays = 3, bool WarningAlerts = true)
{
    public void Validate()
    {
        if (IntervalMinutes is < 1 or > 1440 || PeriodDays is < 7 or > 90 || MinimumSamples is < 2 or > 10000
            || PercentageChange is < 10 or > 1000 || AbsoluteChange is < 1 or > 100000000
            || StalledDays is < 1 or > 365 || ApproachingDays is < 1 or > 30) throw new BusinessException("monitoringInvalidConfiguration");
    }
}
public sealed record MonitoringConfiguration(int Version, MonitoringSettings Settings);
public sealed class MonitoringRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "inventory";
    public string Settings { get; set; } = "";
    public bool Enabled { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastEventAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextEventAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class MonitoringRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public Guid RequestKey { get; set; }
    public Guid? RequestedBy { get; set; }
    public string Trigger { get; set; } = "scheduled";
    public string Identity { get; set; } = MonitoringScope.Identity;
    public string State { get; set; } = "queued";
    public string Settings { get; set; } = "";
    public int Attempts { get; set; }
    public string? Lease { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long DurationMs { get; set; }
    public int Findings { get; set; }
    public string? Error { get; set; }
    public string Diagnostics { get; set; } = "";
}
public sealed class OperationalAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public Guid RunId { get; set; }
    public string Key { get; set; } = "";
    public string Category { get; set; } = "";
    public string Severity { get; set; } = "warning";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public Guid? OwnerId { get; set; }
    public string Label { get; set; } = "";
    public string Facts { get; set; } = "{}";
    public DateTimeOffset? DueAt { get; set; }
    public string Status { get; set; } = "open";
    public bool ConditionActive { get; set; } = true;
    public int Occurrence { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class AlertHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public string Action { get; set; } = "created";
    public Guid? ActorId { get; set; }
    public string Identity { get; set; } = MonitoringScope.Identity;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class MonitoringReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public Guid UserId { get; set; }
    public int Occurrence { get; set; }
    public string Purpose { get; set; } = "explain";
    public Guid ExecutionId { get; set; }
}
public sealed record MonitoringFinding(string Key, string Category, string Severity, string EntityType, Guid? EntityId,
    string Label, Dictionary<string, decimal> Metrics, DateTimeOffset? DueAt = null, Guid? OwnerId = null);
public sealed record MonitoringAnalysis(List<MonitoringFinding> Findings, bool Complete, List<string> Diagnostics);
public sealed record MonitoringScope(string ServiceIdentity, string DataScope)
{
    public const string Identity = "service:erp-monitor-v1";
    public static readonly MonitoringScope Reports = new(Identity, "workspace:reporting-read");
    public void Demand() { if (this != Reports) throw new BusinessException("monitoringScopeRejected", 403); }
}
public static class MonitoringModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<MonitoringRule>(e => { e.ToTable("MonitoringRules"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(16); e.HasIndex(x => x.Kind).IsUnique(); e.Property(x => x.Settings).HasMaxLength(2000); });
        model.Entity<MonitoringRun>(e => {
            e.ToTable("MonitoringRuns"); e.HasKey(x => x.Id); e.HasOne<MonitoringRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RuleId, x.RequestKey }).IsUnique();
            e.HasIndex(x => x.RuleId).IsUnique().HasFilter("\"State\" IN ('queued','running','retry')");
            e.HasIndex(x => new { x.State, x.AvailableAt }); e.Property(x => x.State).HasMaxLength(16); e.Property(x => x.Trigger).HasMaxLength(16);
            e.Property(x => x.Identity).HasMaxLength(64); e.Property(x => x.Settings).HasMaxLength(2000); e.Property(x => x.Error).HasMaxLength(64); e.Property(x => x.Diagnostics).HasMaxLength(2000); e.Property(x => x.Lease).HasMaxLength(32);
        });
        model.Entity<OperationalAlert>(e => {
            e.ToTable("OperationalAlerts"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.RuleId, x.Key }).IsUnique(); e.HasIndex(x => new { x.OwnerId, x.Status, x.LastSeenAt });
            e.Property(x => x.Key).HasMaxLength(160); e.Property(x => x.Category).HasMaxLength(48); e.Property(x => x.Severity).HasMaxLength(16); e.Property(x => x.EntityType).HasMaxLength(24);
            e.Property(x => x.Label).HasMaxLength(160); e.Property(x => x.Status).HasMaxLength(16); e.Property(x => x.Facts).HasMaxLength(4000);
            e.HasOne<MonitoringRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AlertHistory>(e => { e.ToTable("OperationalAlertHistory"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.AlertId, x.CreatedAt }); e.Property(x => x.Action).HasMaxLength(32); e.Property(x => x.Identity).HasMaxLength(64); e.HasOne<OperationalAlert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade); });
        model.Entity<MonitoringReview>(e => { e.ToTable("MonitoringReviews"); e.HasKey(x => x.Id); e.Property(x => x.Purpose).HasMaxLength(16); e.HasIndex(x => new { x.AlertId, x.UserId, x.Occurrence, x.Purpose }).IsUnique(); e.HasOne<OperationalAlert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade); e.HasOne<AiExecution>().WithMany().HasForeignKey(x => x.ExecutionId).OnDelete(DeleteBehavior.Cascade); });
    }
}
