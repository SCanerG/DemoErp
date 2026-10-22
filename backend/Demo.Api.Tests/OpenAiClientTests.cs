using System.Net;
using System.Text;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.Errors;
using Microsoft.Extensions.Configuration;

namespace Demo.Api.Tests;

// The real official SDK uses this intercepted HTTP transport. No live request or real credential is used.
internal sealed class ResponsesTransport(Func<int, HttpResponseMessage> response) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<string> Payloads { get; } = [];
    public Uri? Endpoint { get; private set; }
    public string? Authorization { get; private set; }
    public Func<CancellationToken, Task>? Wait { get; init; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++; Endpoint = request.RequestUri; Authorization = request.Headers.Authorization?.ToString();
        Payloads.Add(await request.Content!.ReadAsStringAsync(ct));
        if (Wait is not null) await Wait(ct);
        return response(Calls);
    }
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    public static object Function(string id = "call_1", string name = "get_inventory_overview", string arguments = "{}") =>
        new { type = "function_call", id = "fc_" + id, call_id = id, name, arguments, status = "completed" };
    public static object Message(string text = "{\"answer\":\"Stok raporu hazır.\",\"language\":\"tr\"}") =>
        new { type = "message", id = "msg_1", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } } };
    public static string Envelope(object[] output, string status = "completed", object? error = null) => JsonSerializer.Serialize(new {
        id = "resp_test", @object = "response", created_at = 1720000000, model = "configured-model", status,
        error, incomplete_details = status == "incomplete" ? new { reason = "max_output_tokens" } : null, output,
        usage = new { input_tokens = 10, output_tokens = 5, total_tokens = 15 }, parallel_tool_calls = false
    });
}
public sealed class OpenAiClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static AiAssistantOptions Settings => new() { Enabled = true, ApiKey = "synthetic-sdk-key-for-intercepted-http", Model = "configured-model" };
    [Fact]
    public async Task Official_Responses_sdk_sends_strict_tools_schema_configured_model_and_no_store()
    {
        using var transport = new ResponsesTransport(_ => ResponsesTransport.Json(ResponsesTransport.Envelope([ResponsesTransport.Function()])));
        using var http = new HttpClient(transport); var client = new OpenAiModelClient(Settings, http);
        var turn = await client.Complete([new("system", "Authorized reports only"), new("user", "Stock")], AiToolRegistry.All, Ct);
        Assert.Equal("call_1", Assert.Single(turn.Calls).Id); Assert.Equal(15, turn.TotalTokens);
        Assert.Equal("https://api.openai.com/v1/responses", transport.Endpoint!.ToString());
        Assert.Equal("Bearer " + Settings.ApiKey, transport.Authorization); Assert.DoesNotContain(Settings.ApiKey, transport.Payloads[0]);
        using var doc = JsonDocument.Parse(transport.Payloads[0]); var root = doc.RootElement;
        Assert.Equal("configured-model", root.GetProperty("model").GetString()); Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.GetProperty("parallel_tool_calls").GetBoolean()); Assert.Equal(1000, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.True(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        var tools = root.GetProperty("tools"); Assert.Equal(9, tools.GetArrayLength());
        foreach (var tool in tools.EnumerateArray()) { Assert.Equal("function", tool.GetProperty("type").GetString()); Assert.True(tool.GetProperty("strict").GetBoolean()); Assert.False(tool.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean()); }
    }
    [Fact]
    public async Task Stateless_reasoning_and_multiple_function_items_are_replayed_with_matching_call_outputs()
    {
        using var transport = new ResponsesTransport(i => ResponsesTransport.Json(i == 1 ? ResponsesTransport.Envelope([
            new { type="reasoning", id="rs_1", encrypted_content="opaque-test-ciphertext", summary=Array.Empty<object>() },
            ResponsesTransport.Function(), ResponsesTransport.Function("call_2","get_pending_orders","{\"limit\":2}")]) : ResponsesTransport.Envelope([ResponsesTransport.Message()])));
        using var http = new HttpClient(transport); var client = new OpenAiModelClient(Settings,http);
        var history = new List<AiModelMessage> { new("system","Turkish"),new("user","Stok") };
        var first = await client.Complete(history,AiToolRegistry.All,Ct);Assert.Equal(2,first.Calls.Count);
        history.Add(new("assistant","",first.Calls));history.Add(new("tool","{\"totalProducts\":2}",CallId:"call_1"));history.Add(new("tool","{}",CallId:"call_2"));
        var final=await client.Complete(history,[],Ct);Assert.Equal("tr",final.Language);Assert.Equal("Stok raporu hazır.",final.Answer);
        using var doc=JsonDocument.Parse(transport.Payloads[1]);var items=doc.RootElement.GetProperty("input").EnumerateArray().ToList();
        Assert.Equal(2,items.Count(i=>i.GetProperty("type").GetString()=="function_call"));
        Assert.Equal(2,items.Count(i=>i.GetProperty("type").GetString()=="function_call_output"));
        Assert.Contains(items,i=>i.GetProperty("type").GetString()=="reasoning" && i.GetProperty("encrypted_content").GetString()=="opaque-test-ciphertext");
        Assert.DoesNotContain("previous_response_id",transport.Payloads[1]);Assert.DoesNotContain(Settings.ApiKey,transport.Payloads[1]);
    }
    [Fact]
    public async Task Persisted_plain_assistant_history_is_valid_Responses_input()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(ResponsesTransport.Envelope([ResponsesTransport.Message()])));
        using var http=new HttpClient(transport);var client=new OpenAiModelClient(Settings,http);
        await client.Complete([new("system","Turkish"),new("user","Old"),new("assistant","Historical answer"),new("user","Current")],[],Ct);
        Assert.Contains("Historical answer",transport.Payloads[0]);Assert.Contains("output_text",transport.Payloads[0]);
    }
    [Theory]
    [InlineData(429,null,"aiProviderRateLimit",429)]
    [InlineData(500,null,"aiProviderUnavailable",503)]
    [InlineData(401,null,"aiProviderAuthentication",503)]
    [InlineData(403,null,"aiProviderAuthentication",503)]
    [InlineData(404,null,"aiModelUnavailable",503)]
    [InlineData(400,"model_not_found","aiModelUnavailable",503)]
    [InlineData(429,"insufficient_quota","aiProviderQuota",503)]
    [InlineData(400,null,"aiProviderRejected",502)]
    public async Task Provider_errors_have_safe_categories_and_zero_hidden_SDK_retries(int status,string? code,string expected,int publicStatus)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(JsonSerializer.Serialize(new {error=new {message="private-response-body",code}}),(HttpStatusCode)status));
        using var http=new HttpClient(transport);var client=new OpenAiModelClient(Settings,http);
        var error=await Assert.ThrowsAnyAsync<BusinessException>(()=>client.Complete([new("user","Stock")],[],Ct));
        Assert.Equal(expected,error.Code);Assert.Equal(publicStatus,error.Status);Assert.Null(error.InnerException);Assert.DoesNotContain("private-response-body",error.ToString());Assert.Equal(1,transport.Calls);
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    public async Task Malformed_envelopes_fail_safely(string body)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(body));using var http=new HttpClient(transport);
        Assert.Equal("aiInvalidResponse",(await Assert.ThrowsAnyAsync<BusinessException>(()=>new OpenAiModelClient(Settings,http).Complete([new("user","Stock")],[],Ct))).Code);
    }
    [Theory]
    [InlineData("{\"answer\":\"Ready\",\"language\":\"en\",\"extra\":1}")]
    [InlineData("{\"answer\":\"a\",\"answer\":\"b\",\"language\":\"en\"}")]
    [InlineData("{\"answer\":\"a\",\"language\":\"de\"}")]
    [InlineData("{}")]
    public async Task Responses_text_requires_local_strict_schema_validation(string content)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(ResponsesTransport.Envelope([ResponsesTransport.Message(content)])));using var http=new HttpClient(transport);
        Assert.Equal("aiInvalidResponse",(await Assert.ThrowsAnyAsync<BusinessException>(()=>new OpenAiModelClient(Settings,http).Complete([new("user","Stock")],[],Ct))).Code);
    }
    [Theory]
    [InlineData("incomplete","aiIncompleteResponse")]
    [InlineData("in_progress","aiProviderUnavailable")]
    public async Task Noncompleted_responses_never_dispatch_partially_generated_tool_calls(string status,string expected)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(ResponsesTransport.Envelope([ResponsesTransport.Function()],status)));using var http=new HttpClient(transport);
        Assert.Equal(expected,(await Assert.ThrowsAnyAsync<BusinessException>(()=>new OpenAiModelClient(Settings,http).Complete([new("user","Stock")],AiToolRegistry.All,Ct))).Code);
    }
    [Fact]
    public async Task Refusals_are_categorized_without_exposing_the_provider_text()
    {
        var refused=new {type="message",id="msg_refusal",role="assistant",status="completed",content=new[]{new{type="refusal",refusal="private-refusal-content"}}};
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(ResponsesTransport.Envelope([refused])));using var http=new HttpClient(transport);
        var error=await Assert.ThrowsAsync<AiProviderException>(()=>new OpenAiModelClient(Settings,http).Complete([new("user","Stock")],[],Ct));
        Assert.Equal("aiRefused",error.Code);Assert.Equal(15,error.TotalTokens);Assert.DoesNotContain("private-refusal-content",error.ToString());
    }
    [Fact]
    public async Task Hosted_tools_are_rejected_even_if_a_provider_attempts_to_emit_them()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(ResponsesTransport.Envelope([new{type="web_search_call",id="ws_1",status="completed",action=new{type="search",query="private"}}])));using var http=new HttpClient(transport);
        Assert.Equal("aiInvalidResponse",(await Assert.ThrowsAnyAsync<BusinessException>(()=>new OpenAiModelClient(Settings,http).Complete([new("user","Stock")],[],Ct))).Code);
    }
    [Fact]
    public async Task Retry_after_is_respected_and_cancellation_prevents_a_second_attempt()
    {
        var firstAttempt=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport=new ResponsesTransport(_=>{var r=ResponsesTransport.Json("{\"error\":{\"code\":\"rate_limit_exceeded\"}}",HttpStatusCode.TooManyRequests);r.Headers.Add("Retry-After","60");firstAttempt.TrySetResult();return r;});
        using var http=new HttpClient(transport);using var cancel=CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pending=AiProviderRunner.Complete(new OpenAiModelClient(Settings,http),[new("user","Stock")],[],Settings,()=>{},cancel.Token);
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10),Ct);cancel.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await pending);Assert.Equal(1,transport.Calls);
    }
    [Fact]
    public async Task Http_cancellation_is_propagated_without_leaking_transport_errors()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json("{}")){Wait=ct=>Task.Delay(Timeout.Infinite,ct)};using var http=new HttpClient(transport);
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(Ct);cancel.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new OpenAiModelClient(Settings,http).Complete([new("user","Stock")],[],cancel.Token));
    }
    [Fact]
    public async Task SDK_network_timeout_is_reported_as_a_safe_timeout_not_a_malformed_answer()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json("{}")){Wait=ct=>Task.Delay(Timeout.Infinite,ct)};
        using var http=new HttpClient(transport);var options=new AiAssistantOptions {ApiKey=Settings.ApiKey,TimeoutSeconds=1};
        var error=await Assert.ThrowsAnyAsync<BusinessException>(()=>new OpenAiModelClient(options,http).Complete([new("user","Stock")],[],Ct));
        Assert.Equal("aiTimeout",error.Code);Assert.Equal(504,error.Status);Assert.Null(error.InnerException);
    }
    [Theory]
    [InlineData("TimeoutSeconds","invalid")]
    [InlineData("MaxOutputTokens","9999999")]
    [InlineData("MaxToolCalls","6")]
    [InlineData("Endpoint","http://internal-server")]
    [InlineData("Endpoint","https://attacker.example/v1")]
    [InlineData("Provider","Unknown")]
    [InlineData("RequestsPerDay","0")]
    public void Invalid_optional_configuration_is_unavailable_without_throwing(string field,string value)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"AiAssistant:Enabled","true"},{"AiAssistant:ApiKey","synthetic-configuration-key"},{"AiAssistant:"+field,value}}).Build();
        Assert.False(AiAssistantOptions.Read(config).Configured);
    }
    [Fact]
    public void Canonical_server_OpenAI_settings_override_legacy_values_without_entering_status()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"OPENAI_API_KEY","synthetic-canonical-key"},{"OPENAI_MODEL","ft:configured:model"},{"AiAssistant:ApiKey","synthetic-legacy-key"},{"AiAssistant:Model","legacy-model"}}).Build();
        var options=AiAssistantOptions.Read(config);Assert.True(options.Configured);Assert.Equal("synthetic-canonical-key",options.ApiKey);Assert.Equal("ft:configured:model",options.Model);
    }
}
