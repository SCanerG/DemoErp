using System.Net;
using System.Text;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.Errors;
using Microsoft.Extensions.Configuration;

namespace Demo.Api.Tests;

// Real official SDK, intercepted HTTP transport: no network or billable provider call.
public sealed class OpenAiClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class Transport(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Payload { get; private set; }
        public Uri? Endpoint { get; private set; }
        public string? Authorization { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Payload = await request.Content!.ReadAsStringAsync(ct); Endpoint = request.RequestUri; Authorization = request.Headers.Authorization?.ToString();
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    [Fact]
    public async Task Official_sdk_sends_strict_tools_backend_credentials_and_bounded_output()
    {
        using var transport = new Transport(HttpStatusCode.OK,
            """{"id":"chatcmpl-test","object":"chat.completion","created":1720000000,"model":"gpt-4.1-mini-2025-04-14","choices":[{"index":0,"message":{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"get_inventory_overview","arguments":"{}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}""");
        using var http = new HttpClient(transport); var client = new OpenAiModelClient(new() { Enabled = true, ApiKey = "test-only-sdk-credential" }, http);
        var turn = await client.Complete([new("system", "Use authorized tools."), new("user", "Stock overview")], AiToolRegistry.All, Ct);
        Assert.Equal("get_inventory_overview", Assert.Single(turn.Calls).Name); Assert.Equal(15, turn.TotalTokens);
        Assert.Equal("https://api.openai.com/v1/chat/completions", transport.Endpoint!.ToString()); Assert.Equal("Bearer test-only-sdk-credential", transport.Authorization);
        Assert.DoesNotContain("test-only-sdk-credential", transport.Payload!);
        using var payload = JsonDocument.Parse(transport.Payload!); Assert.False(payload.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal(1000, payload.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(9, payload.RootElement.GetProperty("tools").GetArrayLength());
        foreach (var tool in payload.RootElement.GetProperty("tools").EnumerateArray())
        { Assert.True(tool.GetProperty("function").GetProperty("strict").GetBoolean()); Assert.False(tool.GetProperty("function").GetProperty("parameters").GetProperty("additionalProperties").GetBoolean()); }
    }
    [Fact]
    public async Task Tool_outputs_are_correlated_and_final_language_is_parsed()
    {
        using var transport = new Transport(HttpStatusCode.OK,
            """{"id":"chatcmpl-test","object":"chat.completion","created":1720000000,"model":"gpt-4.1-mini-2025-04-14","choices":[{"index":0,"message":{"role":"assistant","content":"{\"answer\":\"Stok raporu hazır.\",\"language\":\"tr\"}"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(transport); var client = new OpenAiModelClient(new() { ApiKey = "test-only-sdk-credential" }, http);
        var turn = await client.Complete([new("system", "Turkish"), new("user", "Stok"), new("assistant", "", [new("call_1", "get_inventory_overview", "{}")]),
            new("tool", "{\"totalProducts\":2}", CallId: "call_1")], [], Ct);
        Assert.Equal("tr", turn.Language); Assert.Equal("Stok raporu hazır.", turn.Answer); Assert.Empty(turn.Calls);
        Assert.Contains("tool_call_id", transport.Payload!); Assert.Contains("call_1", transport.Payload!);
    }
    [Theory]
    [InlineData(429, "aiProviderRateLimit", 429)]
    [InlineData(500, "aiProviderUnavailable", 503)]
    [InlineData(401, "aiProviderUnavailable", 503)]
    [InlineData(400, "aiProviderUnavailable", 503)]
    public async Task Provider_errors_are_sanitized_and_not_retried(int status, string code, int publicStatus)
    {
        using var transport = new Transport((HttpStatusCode)status, """{"error":{"message":"private-provider-content test-only-sdk-credential","type":"provider_error"}}""");
        using var http = new HttpClient(transport); var client = new OpenAiModelClient(new() { ApiKey = "test-only-sdk-credential" }, http);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => client.Complete([new("user", "Stock")], [], Ct));
        Assert.Equal(code, ex.Code); Assert.Equal(publicStatus, ex.Status); Assert.Null(ex.InnerException); Assert.DoesNotContain("credential", ex.ToString()); Assert.Equal(1, transport.Calls);
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    public async Task Malformed_wire_responses_fail_safely(string response)
    {
        using var transport = new Transport(HttpStatusCode.OK, response); using var http = new HttpClient(transport);
        var client = new OpenAiModelClient(new() { ApiKey = "test-only-sdk-credential" }, http);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => client.Complete([new("user", "Stock")], [], Ct)); Assert.Equal("aiInvalidResponse", ex.Code);
    }
    [Theory]
    [InlineData("TimeoutSeconds", "invalid")]
    [InlineData("MaxOutputTokens", "9999999")]
    [InlineData("MaxToolCalls", "6")]
    [InlineData("Endpoint", "http://internal-server")]
    [InlineData("Endpoint", "https://attacker.example/v1")]
    [InlineData("Provider", "Unknown")]
    [InlineData("RequestsPerDay", "0")]
    public void Invalid_optional_configuration_is_unavailable_without_throwing(string field, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AiAssistant:Enabled"] = "true", ["AiAssistant:ApiKey"] = "test-only-sdk-credential", [$"AiAssistant:{field}"] = value }).Build();
        var options = AiAssistantOptions.Read(config); Assert.True(options.Enabled); Assert.False(options.Configured);
    }
}
