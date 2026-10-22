using System.ClientModel;
using System.ClientModel.Primitives;
using Demo.Api.Errors;
using OpenAI.Responses;

namespace Demo.Api.AI;

// The pinned official SDK marks Responses types OPENAI001. Keep that API surface inside this adapter.
#pragma warning disable OPENAI001
public sealed class OpenAiModelClient(AiAssistantOptions settings, HttpClient httpClient) : IAiModelClient
{
    // Scoped to one HTTP request. Opaque reasoning/function items are never persisted, logged or sent to the UI.
    private readonly Dictionary<string, IReadOnlyList<ResponseItem>> continuations = new(StringComparer.Ordinal);
    public async Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var client = new ResponsesClient(new ApiKeyCredential(settings.ApiKey), new ResponsesClientOptions {
                Endpoint = new Uri(settings.Endpoint), Transport = new HttpClientPipelineTransport(httpClient),
                NetworkTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds), RetryPolicy = new ClientRetryPolicy(0) });
            var input = new List<ResponseItem>();
            foreach (var message in messages)
            {
                switch (message.Role)
                {
                    case "system": input.Add(ResponseItem.CreateSystemMessageItem(message.Text)); break;
                    case "user": input.Add(ResponseItem.CreateUserMessageItem(message.Text)); break;
                    case "tool":
                        if (string.IsNullOrWhiteSpace(message.CallId)) throw new BusinessException("aiInvalidResponse", 502);
                        input.Add(new FunctionCallOutputResponseItem(message.CallId, message.Text)); break;
                    case "assistant" when message.Calls?.Count > 0:
                        if (continuations.TryGetValue(message.Calls[0].Id, out var items))
                        {
                            var cached = items.OfType<FunctionCallResponseItem>().Select(c => new AiToolCall(c.CallId, c.FunctionName, c.FunctionArguments.ToString()));
                            if (!cached.SequenceEqual(message.Calls)) throw new BusinessException("aiInvalidResponse", 502);
                            input.AddRange(items);
                        }
                        else input.AddRange(message.Calls.Select(c => new FunctionCallResponseItem(c.Id, c.Name, BinaryData.FromString(c.Arguments))));
                        break;
                    case "assistant": input.Add(ResponseItem.CreateAssistantMessageItem(message.Text)); break;
                    default: throw new BusinessException("aiInvalidResponse", 502);
                }
            }
            var options = new CreateResponseOptions(settings.Model, input) {
                StoredOutputEnabled = false, BackgroundModeEnabled = false, StreamingEnabled = false,
                ParallelToolCallsEnabled = false, MaxOutputTokenCount = settings.MaxOutputTokens,
                TextOptions = new ResponseTextOptions { TextFormat = ResponseTextFormat.CreateJsonSchemaFormat("business_answer", BinaryData.FromString(AiOutputValidator.Schema), null, true) }
            };
            options.IncludedProperties.Add(new IncludedResponseProperty("reasoning.encrypted_content"));
            foreach (var tool in tools) options.Tools.Add(ResponseTool.CreateFunctionTool(tool.Name, BinaryData.FromString(tool.Schema), true, tool.Description));
            ResponseResult result = await client.CreateResponseAsync(options, ct);
            var tokens = result.Usage?.TotalTokenCount;
            if (result.Status is null || result.Object != "response" || string.IsNullOrWhiteSpace(result.Id)) throw new BusinessException("aiInvalidResponse", 502);
            if (result.Status == ResponseStatus.Incomplete) throw new AiProviderException("aiIncompleteResponse", 502, totalTokens: tokens);
            if (result.Status != ResponseStatus.Completed || result.Error is not null)
                throw Failure(result.Error?.Code.ToString(), 503, tokens);
            if (result.OutputItems.Count is < 1 or > 32) throw new BusinessException("aiInvalidResponse", 502);
            foreach (var item in result.OutputItems)
            {
                if (item is MessageResponseItem message)
                {
                    if (message.Status is { } messageStatus && !string.Equals(messageStatus.ToString(), "completed", StringComparison.OrdinalIgnoreCase)) throw new BusinessException("aiInvalidResponse", 502);
                    if (message.Role != MessageRole.Assistant || message.Content.Count is < 1 or > 8) throw new BusinessException("aiInvalidResponse", 502);
                    if (message.Content.Any(c => c.Kind == ResponseContentPartKind.Refusal)) throw new AiProviderException("aiRefused", 422, totalTokens: tokens);
                    if (message.Content.Any(c => c.Kind != ResponseContentPartKind.OutputText)) throw new BusinessException("aiInvalidResponse", 502);
                }
                else if (item is ReasoningResponseItem reasoning)
                {
                    if (reasoning.EncryptedContent?.Length > 100000 || reasoning.SummaryParts.Count > 8) throw new BusinessException("aiInvalidResponse", 502);
                }
                else if (item is FunctionCallResponseItem function) {
                    if (function.Status is { } functionStatus && !string.Equals(functionStatus.ToString(), "completed", StringComparison.OrdinalIgnoreCase)) throw new BusinessException("aiInvalidResponse", 502);
                }
                else throw new BusinessException("aiInvalidResponse", 502);
            }
            var calls = result.OutputItems.OfType<FunctionCallResponseItem>().Select(c => new AiToolCall(c.CallId, c.FunctionName, c.FunctionArguments.ToString())).ToList();
            if (calls.Count > 0)
            {
                if (calls.Count > settings.MaxToolCalls || calls.Any(c => string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 100 || c.Arguments.Length > 2000)
                    || calls.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != calls.Count) throw new BusinessException("aiInvalidResponse", 502);
                continuations[calls[0].Id] = result.OutputItems.ToArray();
                return new(null, null, calls, tokens);
            }
            var parsed = AiOutputValidator.Parse(result.GetOutputText());
            return new(parsed.Answer, parsed.Language, [], tokens);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new AiProviderException("aiTimeout", 504); }
        catch (BusinessException) { throw; }
        catch (ClientResultException ex)
        {
            // Inspect only allowlisted categories and retry headers; never expose response body or inner exceptions.
            var response = ex.GetRawResponse();
            string? code = null;
            try {
                if (response?.Content is { } content && content.ToMemory().Length <= 16000) {
                    using var json = System.Text.Json.JsonDocument.Parse(content);
                    if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var property)) code = property.GetString();
                }
            } catch (Exception) { /* Invalid provider errors remain sanitized. */ }
            TimeSpan? delay = null;
            if (response?.Headers.TryGetValue("Retry-After", out var header) == true) {
                if (int.TryParse(header, out var seconds) && seconds >= 0) delay = TimeSpan.FromSeconds(Math.Min(seconds, 86400));
                else if (DateTimeOffset.TryParse(header, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
                    delay = TimeSpan.FromTicks(Math.Clamp((date - DateTimeOffset.UtcNow).Ticks, 0, TimeSpan.FromDays(1).Ticks));
            }
            throw Failure(code, ex.Status, retryAfter: delay);
        }
        catch (HttpRequestException) { throw new AiProviderException("aiProviderUnavailable", 503); }
        catch (Exception) { throw new BusinessException("aiInvalidResponse", 502); }
    }
    private static AiProviderException Failure(string? code, int status, int? totalTokens = null, TimeSpan? retryAfter = null) => code switch {
        "invalid_api_key" or "authentication_error" => new("aiProviderAuthentication", 503, totalTokens: totalTokens),
        "model_not_found" or "model_not_available" => new("aiModelUnavailable", 503, totalTokens: totalTokens),
        "rate_limit_exceeded" => new("aiProviderRateLimit", 429, retryAfter, totalTokens),
        "insufficient_quota" => new("aiProviderQuota", 503, totalTokens: totalTokens),
        _ => status switch {
            401 or 403 => new("aiProviderAuthentication", 503, totalTokens: totalTokens),
            404 => new("aiModelUnavailable", 503, totalTokens: totalTokens),
            429 => new("aiProviderRateLimit", 429, retryAfter, totalTokens),
            0 or 408 or 409 or >= 500 => new("aiProviderUnavailable", 503, retryAfter, totalTokens),
            _ => new("aiProviderRejected", 502, totalTokens: totalTokens)
        }
    };
}
#pragma warning restore OPENAI001
