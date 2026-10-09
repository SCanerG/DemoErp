using Demo.Api.Errors;
using Microsoft.Extensions.Caching.Memory;

namespace Demo.Api.AI;

// Single-instance demo quota. Bounded, expiring state; never stores prompts or results.
public sealed class AiRequestQuota(AiAssistantOptions options) : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly object gate = new();
    private sealed record Usage(long Minute, DateOnly Day, int MinuteCount, int DayCount);
    public void Consume(Guid user, DateTimeOffset now)
    {
        lock (gate)
        {
            var minute = now.ToUnixTimeSeconds() / 60;
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            cache.TryGetValue<Usage>(user, out var usage);
            var minutes = usage?.Minute == minute ? usage.MinuteCount : 0;
            var days = usage?.Day == day ? usage.DayCount : 0;
            if (minutes >= options.RequestsPerMinute || days >= options.RequestsPerDay) throw new BusinessException("aiRateLimit", 429);
            cache.Set(user, new Usage(minute, day, minutes + 1, days + 1), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });
            if (!cache.TryGetValue(user, out Usage? _)) throw new BusinessException("aiRateLimit", 429);
        }
    }
    public void Dispose() => cache.Dispose();
}
