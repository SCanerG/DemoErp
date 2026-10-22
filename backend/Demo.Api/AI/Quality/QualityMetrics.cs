namespace Demo.Api.AI.Quality;

public sealed record LatencySummary(int Samples, double? AverageMs, double? P50Ms, double? P95Ms, double? P99Ms);
public static class QualityMetrics
{
    // Conservative display floors, not guarantees of statistical confidence. No interpolation from tiny samples.
    public static LatencySummary Latency(IEnumerable<double> values)
    {
        var rows = values.Where(v => double.IsFinite(v) && v >= 0).Order().ToArray();
        double? At(double p, int minimum) => rows.Length < minimum ? null : rows[Math.Clamp((int)Math.Ceiling(rows.Length * p) - 1, 0, rows.Length - 1)];
        return new(rows.Length, rows.Length == 0 ? null : rows.Average(), At(.5, 20), At(.95, 100), At(.99, 1000));
    }
    public static object Summarize(IReadOnlyList<QualitySpanRow> rows)
    {
        var requests = rows.Where(s => s.Operation == "request").ToArray(); var providers = rows.Where(s => s.Operation == "provider").ToArray(); var tools = rows.Where(s => s.Operation == "tool").ToArray();
        static double? Rate(IEnumerable<QualitySpanRow> source, Func<QualitySpanRow, bool> predicate) { var values = source.ToArray(); return values.Length == 0 ? null : values.Count(predicate) / (double)values.Length; }
        var overhead = rows.Where(s => s.Operation == "orchestration").Select(parent => {
            var children = rows.Where(c => c.TraceId == parent.TraceId && c.ParentSpanId == parent.SpanId).OrderBy(c => c.StartedAt).ToArray();
            var end = parent.StartedAt; double covered = 0;
            foreach (var child in children) { var start = child.StartedAt > end ? child.StartedAt : end; var childEnd = child.StartedAt.AddMilliseconds(child.DurationMs); var parentEnd = parent.StartedAt.AddMilliseconds(parent.DurationMs); if(childEnd>parentEnd)childEnd=parentEnd; if(childEnd>start)covered+=(childEnd-start).TotalMilliseconds; if(childEnd>end)end=childEnd; }
            return Math.Max(0, parent.DurationMs-covered);
        }).ToArray();
        return new {
            observedSpans = rows.Count, requests = requests.Length, latency = Latency(requests.Select(s => (double)s.DurationMs)),
            successRate = Rate(requests, s => s.Outcome is "success" or "fallback" or "mock" or "insufficient"), errorRate = Rate(requests, s => s.Outcome is "failure" or "timeout"),
            orchestrationUnattributedLatency = Latency(overhead),
            cancellationRate = Rate(requests, s => s.Outcome == "cancelled"), timeoutRate = Rate(requests, s => s.Outcome == "timeout" || s.Error is "aiTimeout" or "knowledgeTimeout"),
            fallbackRate = Rate(requests, s => s.Outcome == "fallback"), toolSuccessRate = Rate(tools, s => s.Outcome == "success"),
            providerRetryAttemptRate = Rate(providers, s => s.Retry > 0), reportedTotalTokens = providers.Any(s => s.Tokens.HasValue) ? providers.Sum(s => (long)(s.Tokens ?? 0)) : (long?)null,
            providerCallsWithUsage = providers.Count(s => s.Tokens.HasValue), providerCallsWithoutUsage = providers.Count(s => !s.Tokens.HasValue),
            agents = rows.Where(s => s.Operation == "agent").GroupBy(s => s.Component).Select(g => new { agent = g.Key, calls = g.Count(), successRate = Rate(g, s => s.Outcome == "success"), latency = Latency(g.Select(s => (double)s.DurationMs)) }),
            operations = rows.GroupBy(s => s.Operation).Select(g => new { operation = g.Key, calls = g.Count(), latency = Latency(g.Select(s => (double)s.DurationMs)) }),
            providers = providers.GroupBy(s => new { s.Provider, s.Model, s.Environment }).Select(g => new { g.Key.Provider, g.Key.Model, g.Key.Environment, calls = g.Count(), mock = g.Key.Provider == "Mock", latency = Latency(g.Select(s => (double)s.DurationMs)), reportedTokens = g.Any(s => s.Tokens.HasValue) ? g.Sum(s => (long)(s.Tokens ?? 0)) : (long?)null }),
            errors = rows.Where(s => s.Error != null).GroupBy(s => s.Error).Select(g => new { category = g.Key, observations = g.Count() })
        };
    }
}
