namespace Demo.Api.AI.Quality;

public sealed class ObservedModelClient(IAiModelClient inner, QualityTelemetry telemetry, AiAssistantOptions options) : IAiModelClient
{
    public async Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken ct)
    {
        using var span = telemetry.Begin("provider", "completion");
        span?.Provider(options.Provider, options.Model); span?.Context(messages.Sum(m => m.Text.Length + (m.Calls?.Sum(c => c.Arguments.Length) ?? 0))); span?.Retry(AiProviderRunner.RetryOrdinal.Value);
        try { var result = await inner.Complete(messages, tools, ct); span?.Usage(result.TotalTokens); if (options.Provider == "Mock") span?.Outcome("mock"); return result; }
        catch (Exception error) { span?.Fail(error); if (error is AiProviderException provider) span?.Usage(provider.TotalTokens); throw; }
    }
}
