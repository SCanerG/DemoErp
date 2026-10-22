using System.Net;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.Errors;
using Microsoft.Extensions.Configuration;

namespace Demo.Api.Tests;

public sealed class OllamaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static AiAssistantOptions Settings => new() { Enabled = true, Provider = "Ollama", AllowDevelopmentMock = true, Model = "qwen3:8b", TimeoutSeconds = 120 };
    private static string Envelope(string message, string reason = "stop") => "{\"done\":true,\"done_reason\":\"" + reason + "\",\"prompt_eval_count\":20,\"eval_count\":10,\"message\":" + message + "}";
    [Fact]
    public void Provider_configuration_is_isolated_from_OpenAI_and_disabled_in_production()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["AiAssistant:Provider"]="Ollama", ["OPENAI_MODEL"]="cloud-model", ["OPENAI_API_KEY"]="synthetic cloud key with spaces"
        }).Build();
        var options = AiAssistantOptions.Read(config,true);
        Assert.True(options.Configured); Assert.Equal("qwen3:8b",options.Model); Assert.Empty(options.ApiKey);
        Assert.False(AiAssistantOptions.Read(config,false).Configured);
    }
    [Theory]
    [InlineData("http://evil.example:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434/redirect")]
    [InlineData("http://user:pass@127.0.0.1:11434")]
    [InlineData("http://127.0.0.1:11435")]
    public void Noncanonical_endpoints_are_rejected(string endpoint) => Assert.False(AiAssistantOptions.IsLocalOllamaEndpoint(endpoint));
    [Fact]
    public async Task Native_tool_roundtrip_replays_named_outputs_without_credentials()
    {
        using var transport = new ResponsesTransport(i => ResponsesTransport.Json(Envelope(i == 1
            ? "{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\"get_inventory_overview\",\"arguments\":{}}}]}"
            : "{\"role\":\"assistant\",\"content\":\"{\\\"answer\\\":\\\"Stok raporu.\\\",\\\"language\\\":\\\"tr\\\"}\"}")));
        using var http = new HttpClient(transport); var client = new OllamaModelClient(Settings,http);
        var history = new List<AiModelMessage> { new("user","Stock") };
        var turn = await client.Complete(history,AiToolRegistry.All,Ct);
        var call = Assert.Single(turn.Calls); Assert.Equal("get_inventory_overview",call.Name); Assert.Equal(30,turn.TotalTokens);
        history.Add(new("assistant","",turn.Calls)); history.Add(new("tool","{}",CallId:call.Id));
        var final = await client.Complete(history,AiToolRegistry.All,Ct); Assert.Equal("tr",final.Language);
        Assert.Equal("http://127.0.0.1:11434/api/chat",transport.Endpoint!.ToString()); Assert.Null(transport.Authorization);
        using var first = JsonDocument.Parse(transport.Payloads[0]); Assert.Equal(9,first.RootElement.GetProperty("tools").GetArrayLength());
        Assert.False(first.RootElement.GetProperty("think").GetBoolean()); Assert.False(first.RootElement.GetProperty("stream").GetBoolean());
        using var second = JsonDocument.Parse(transport.Payloads[1]); Assert.True(second.RootElement.TryGetProperty("format",out _));
        Assert.Contains("tool_name",transport.Payloads[1]); Assert.DoesNotContain("Bearer",transport.Payloads[1]);
    }
    [Theory]
    [InlineData(404,"aiModelUnavailable")]
    [InlineData(429,"aiProviderRateLimit")]
    [InlineData(500,"aiProviderUnavailable")]
    [InlineData(302,"aiProviderRejected")]
    public async Task Provider_errors_are_safe(int status,string code)
    {
        using var transport = new ResponsesTransport(_ => ResponsesTransport.Json("private provider details",(HttpStatusCode)status));
        using var http=new HttpClient(transport);
        var ex=await Assert.ThrowsAsync<AiProviderException>(()=>new OllamaModelClient(Settings,http).Complete([new("user","Stock")],[],Ct));
        Assert.Equal(code,ex.Code); Assert.DoesNotContain("private",ex.Message);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"done\":false,\"done_reason\":\"length\",\"prompt_eval_count\":1,\"eval_count\":2}")]
    [InlineData("{\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":-1,\"eval_count\":2}")]
    public async Task Malformed_or_incomplete_envelopes_are_rejected(string body)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(body)); using var http=new HttpClient(transport);
        await Assert.ThrowsAnyAsync<BusinessException>(()=>new OllamaModelClient(Settings,http).Complete([new("user","Stock")],[],Ct));
    }
    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json("{}")) { Wait = ct=>Task.Delay(Timeout.Infinite,ct) };
        using var http=new HttpClient(transport); using var cts=new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new OllamaModelClient(Settings,http).Complete([new("user","Stock")],[],cts.Token));
    }
}
