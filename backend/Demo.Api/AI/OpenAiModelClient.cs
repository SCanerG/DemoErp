using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Demo.Api.Errors;
using OpenAI;
using OpenAI.Chat;

namespace Demo.Api.AI;

public sealed class OpenAiModelClient(AiAssistantOptions settings, HttpClient httpClient) : IAiModelClient
{
    public async Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken ct)
    {
        try
        {
            var client = new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), new OpenAIClientOptions
            { Endpoint = new Uri(settings.Endpoint), Transport = new HttpClientPipelineTransport(httpClient), NetworkTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds), RetryPolicy = new ClientRetryPolicy(0) });
            var input = new List<ChatMessage>();
            foreach (var m in messages)
                input.Add(m.Role switch
                {
                    "system" => new SystemChatMessage(m.Text), "user" => new UserChatMessage(m.Text),
                    "tool" => new ToolChatMessage(m.CallId!, m.Text),
                    "assistant" when m.Calls?.Count > 0 => new AssistantChatMessage(m.Calls.Select(c =>
                        ChatToolCall.CreateFunctionToolCall(c.Id, c.Name, BinaryData.FromString(c.Arguments)))),
                    _ => throw new BusinessException("aiInvalidResponse", 502)
                });
            var options = new ChatCompletionOptions { MaxOutputTokenCount = settings.MaxOutputTokens,
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("business_answer", BinaryData.FromString(
                    """{"type":"object","properties":{"answer":{"type":"string"},"language":{"type":"string","enum":["en","tr"]}},"required":["answer","language"],"additionalProperties":false}"""), jsonSchemaIsStrict: true) };
            if (tools.Count > 0) options.AllowParallelToolCalls = false;
            foreach (var t in tools) options.Tools.Add(ChatTool.CreateFunctionTool(t.Name, t.Description, BinaryData.FromString(t.Schema), functionSchemaIsStrict: true));
            ChatCompletion result = await client.CompleteChatAsync(input, options, ct);
            if (result.FinishReason == ChatFinishReason.ToolCalls)
                return new(null, null, result.ToolCalls.Select(c => new AiToolCall(c.Id, c.FunctionName, c.FunctionArguments.ToString())).ToList(), result.Usage?.TotalTokenCount);
            if (result.FinishReason != ChatFinishReason.Stop) throw new BusinessException("aiInvalidResponse", 502);
            using var json = JsonDocument.Parse(string.Concat(result.Content.Select(c => c.Text)));
            return new(json.RootElement.GetProperty("answer").GetString(), json.RootElement.GetProperty("language").GetString(), [], result.Usage?.TotalTokenCount);
        }
        catch (OperationCanceledException) { throw; }
        catch (BusinessException) { throw; }
        catch (ClientResultException ex) { throw new BusinessException(ex.Status == 429 ? "aiProviderRateLimit" : "aiProviderUnavailable", ex.Status == 429 ? 429 : 503); }
        catch (Exception) { throw new BusinessException("aiInvalidResponse", 502); }
    }
}
