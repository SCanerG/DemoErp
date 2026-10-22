using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI.Quality;

public sealed class QualityOptions
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 7;
    public int MaxSpans { get; set; } = 50000;
    public int MaxEvaluationRuns { get; set; } = 200;
    public static QualityOptions Read(IConfiguration config)
    {
        var result = new QualityOptions(); config.GetSection("AiQuality").Bind(result);
        if (result.RetentionDays is < 1 or > 30 || result.MaxSpans is < 100 or > 100000 || result.MaxEvaluationRuns is < 2 or > 1000) throw new InvalidOperationException("Invalid quality retention limits.");
        return result;
    }
}
public sealed class QualitySpanRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public Guid? ExecutionId { get; set; }
    public string TraceId { get; set; } = "";
    public string SpanId { get; set; } = "";
    public string ParentSpanId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Component { get; set; } = "";
    public string Outcome { get; set; } = "success";
    public string? Error { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string Environment { get; set; } = "Development";
    public int? Tokens { get; set; }
    public int? ContextCharacters { get; set; }
    public int? RetrievedCount { get; set; }
    public int Retry { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class QualityEvaluationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid RequestKey { get; set; }
    public string Suite { get; set; } = "contracts-v1";
    public string DatasetHash { get; set; } = "";
    public string Mode { get; set; } = "deterministic-fixtures";
    public string Status { get; set; } = "success";
    public string Report { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
}
public static class QualityModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<QualitySpanRow>(e => {
            e.ToTable("AiQualitySpans"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.UserId, x.StartedAt }); e.HasIndex(x => x.StartedAt); e.HasIndex(x => new { x.TraceId, x.UserId }); e.HasIndex(x => x.ExpiresAt);
            e.Property(x => x.TraceId).HasMaxLength(32); e.Property(x => x.SpanId).HasMaxLength(16); e.Property(x => x.ParentSpanId).HasMaxLength(16);
            e.Property(x => x.Operation).HasMaxLength(24); e.Property(x => x.Component).HasMaxLength(64); e.Property(x => x.Outcome).HasMaxLength(24); e.Property(x => x.Error).HasMaxLength(64);
            e.Property(x => x.Provider).HasMaxLength(16); e.Property(x => x.Model).HasMaxLength(100); e.Property(x => x.Environment).HasMaxLength(24);
        });
        model.Entity<QualityEvaluationRun>(e => {
            e.ToTable("AiQualityEvaluations"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); e.HasIndex(x => new { x.UserId, x.CreatedAt }); e.HasIndex(x => x.ExpiresAt);
            e.Property(x => x.Suite).HasMaxLength(32); e.Property(x => x.DatasetHash).HasMaxLength(64); e.Property(x => x.Mode).HasMaxLength(32); e.Property(x => x.Status).HasMaxLength(24); e.Property(x => x.Report).HasMaxLength(100000);
        });
    }
}
