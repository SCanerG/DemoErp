using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.Errors;
using Microsoft.Extensions.DependencyInjection;
namespace Demo.Api.Tests;
public sealed class AiReliabilityTests
{
    [Theory]
    [InlineData("Son 30 günde 4 adet satıldı; stok 0.","{\"days\":30,\"sold\":4,\"stock\":0}","tr")]
    [InlineData("Recorded sales were 1,250.50 USD.","{\"sales\":1250.50}","en")]
    [InlineData("Kayıtlı satış tutarı 1.250,50 USD.","{\"sales\":1250.50}","tr")]
    public void Verified_numerical_values_use_the_response_language(string answer,string facts,string language)=>AiVerifiedNumbers.Validate(answer,facts,language);
    [Fact]
    public void Invented_numerical_value_is_rejected()=>Assert.Equal("unverified_number",Assert.Throws<AiProviderException>(()=>AiVerifiedNumbers.Validate("Stock is 999 units.","{\"stock\":4}","en")).ValidationFailure);
    [Fact]
    public void Adjacent_JSON_array_values_remain_independent()=>AiVerifiedNumbers.Validate("Stock is 4, demand is 5.","[4,5]","en");
    [Fact]
    public async Task Final_phase_tool_request_is_rejected_not_recovered()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json("{\"done\":true,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"function\":{\"name\":\"get_inventory_overview\",\"arguments\":{}}}]}}"));
        using var http=new HttpClient(transport);
        var ex=await Assert.ThrowsAsync<AiProviderException>(()=>new OllamaModelClient(OllamaTests.Settings,http).Complete([new("user","Explain")],[],Ct));
        Assert.Equal("aiUnknownTool",ex.Code);
    }
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static string Envelope(string content,string? extra="",string? reason="stop")=>JsonSerializer.Serialize(new {done=true,done_reason=reason,prompt_eval_count=10,eval_count=5,message=new {role="assistant",content,thinking=extra,tool_calls=(object?)null}});
    [Theory]
    [InlineData("{\"answer\":\"Stok sıfır; yeniden tedarik önerilir.\",\"language\":\"tr\"}")]
    [InlineData("```json\n{\"answer\":\"Stok sıfır.\",\"language\":\"tr\"}\n```")]
    [InlineData("<think>private reasoning must never leave the adapter</think>\n{\"answer\":\"Stok sıfır.\",\"language\":\"tr\"}")]
    public async Task Valid_Qwen_final_variations_are_normalized_without_exposing_thinking(string content)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(Envelope(content,"separate private thinking")));
        using var http=new HttpClient(transport);var diagnostics=new AiResponseDiagnostics();var client=new OllamaModelClient(OllamaTests.Settings,http,diagnostics);
        var turn=await client.Complete([new("system","language=tr"),new("user","Explain verified stock")],[],Ct);
        Assert.Equal("tr",turn.Language);Assert.DoesNotContain("private",turn.Answer);Assert.Equal(15,turn.TotalTokens);Assert.Equal(5,diagnostics.OutputTokens);
        using var request=JsonDocument.Parse(transport.Payloads[0]);var text=request.RootElement.GetProperty("messages").GetRawText();
        Assert.DoesNotContain("Use native tool_calls",text);Assert.True(request.RootElement.TryGetProperty("format",out _));
    }
    [Theory]
    [InlineData("{\"done\":true,\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"answer\\\":\\\"Stok raporu.\\\",\\\"language\\\":\\\"tr\\\"}\"}}")]
    [InlineData("{\"done\":true,\"done_reason\":null,\"message\":{\"role\":\"assistant\",\"tool_calls\":null,\"content\":\"{\\\"answer\\\":\\\"Stok raporu.\\\",\\\"language\\\":\\\"tr\\\"}\"}}")]
    public async Task Optional_usage_reason_and_null_tool_calls_are_not_output_failures(string body)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(body));using var http=new HttpClient(transport);
        var turn=await new OllamaModelClient(OllamaTests.Settings,http).Complete([new("user","Stock")],[],Ct);Assert.Null(turn.TotalTokens);Assert.Equal("tr",turn.Language);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"limit\":100}")]
    public async Task Native_encoded_tool_arguments_are_not_rewritten_or_defaulted(string args)
    {
        var body=JsonSerializer.Serialize(new {done=true,done_reason="stop",prompt_eval_count=1,eval_count=2,message=new {role="assistant",tool_calls=new[]{new{function=new{name="get_low_stock_products",arguments=args}}}}});
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(body));using var http=new HttpClient(transport);
        var turn=await new OllamaModelClient(OllamaTests.Settings,http).Complete([new("user","Stock")],AiToolRegistry.All,Ct);
        var call=Assert.Single(turn.Calls);Assert.Equal(args,call.Arguments);Assert.Throws<BusinessException>(()=>AiToolRegistry.Validate(AiToolRegistry.Find(call.Name),call.Arguments));
    }
    [Theory]
    [InlineData("{","envelope_json")]
    [InlineData("{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"\"}}","empty_answer")]
    [InlineData("{\"done\":true,\"done_reason\":\"length\",\"message\":{\"role\":\"assistant\",\"content\":\"{\"}}","output_truncated")]
    [InlineData("{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"<think>unfinished\"}}","incomplete_thinking")]
    public async Task Invalid_outputs_have_safe_specific_diagnostics(string body,string failure)
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(body));using var http=new HttpClient(transport);
        var diagnostics=new AiResponseDiagnostics();var ex=await Assert.ThrowsAsync<AiProviderException>(()=>new OllamaModelClient(OllamaTests.Settings,http,diagnostics).Complete([new("user","Stock")],[],Ct));
        Assert.Equal(failure,ex.ValidationFailure);Assert.DoesNotContain(body,ex.Message);Assert.Equal(failure,diagnostics.LastValidationFailure);
    }
    [Fact]
    public async Task Native_plain_conversational_answer_maps_without_claiming_report_data()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(Envelope("Merhaba, izinli raporları açıklayabilirim.")));using var http=new HttpClient(transport);
        var turn=await new OllamaModelClient(OllamaTests.Settings,http).Complete([new("system","language=tr"),new("user","Merhaba")],AiToolRegistry.All,Ct);Assert.Equal("tr",turn.Language);Assert.Empty(turn.Calls);
    }
    [Fact]
    public async Task One_final_repair_then_labelled_fallback_preserves_known_usage()
    {
        var calls=0;var kinds=new List<string>();
        var turn=await AiAnswerComposer.Complete(_=>{calls++;throw new AiProviderException("aiInvalidResponse",502,totalTokens:7,validationFailure:"empty_answer");},"tr","",()=>AiTrustedSummary.Tools([new("get_inventory_overview",DateTimeOffset.UtcNow,null,null,"Current","USD",true,new AiInventoryOverview(3,1,1,1))],"tr"),Ct,kinds.Add);
        Assert.Equal(2,calls);Assert.Equal(14,turn.TotalTokens);Assert.Equal("fallback",turn.ResponseKind);Assert.StartsWith(AiAnswerComposer.TurkishFallback,turn.Answer);Assert.Contains("3",turn.Answer);
        Assert.Equal(new[]{"empty_answer","empty_answer"},kinds);
    }
    [Fact]
    public async Task Repaired_final_schema_is_model_generated_and_counted()
    {
        var calls=0;
        var turn=await AiAnswerComposer.Complete(repair=>{calls++;return repair?Task.FromResult(new AiModelTurn("Stok sıfır olduğu için yeniden tedarik önerilir.","tr",[],5)):throw new AiProviderException("aiIncompleteResponse",502,totalTokens:3);},"tr","",()=>throw new Exception("must not fallback"),Ct);
        Assert.Equal(2,calls);Assert.Equal("model",turn.ResponseKind);Assert.Equal(8,turn.TotalTokens);
    }
    [Fact]
    public async Task Unauthorized_final_tool_or_secret_never_triggers_repair_or_fallback()
    {
        foreach(var value in new[]{new AiModelTurn(null,null,[new("x","execute_sql","{}")]),new AiModelTurn("Bearer abcdefghijklmnop0123456789","en",[])}) {
            var calls=0;await Assert.ThrowsAsync<AiUnsafeOutputException>(()=>AiAnswerComposer.Complete(_=>{calls++;return Task.FromResult(value);},"en","",()=>throw new Exception("forbidden fallback"),Ct));Assert.Equal(1,calls);
        }
    }
    [Fact]
    public async Task Timeout_or_context_error_is_not_hidden_by_fallback()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(()=>AiAnswerComposer.Complete(_=>throw new OperationCanceledException(),"en","",()=>"fallback",Ct));
        var ex=await Assert.ThrowsAsync<BusinessException>(()=>AiAnswerComposer.Complete(_=>throw new BusinessException("aiContextLimit",400),"en","",()=>"fallback",Ct));Assert.Equal("aiContextLimit",ex.Code);
    }
}
public sealed partial class AiTests
{
    [Fact]
    public async Task Chat_final_repair_does_not_repeat_tools_and_marks_template_in_history()
    {
        var fake=new FakeModel();fake.Turns.Enqueue(new(null,null,[Call("get_inventory_overview")]));fake.Handler=(messages,tools,_)=>{
            if(messages.Last().Role=="user")return Task.FromResult(new AiModelTurn(null,null,[Call("get_inventory_overview")]));
            throw new AiProviderException("aiInvalidResponse",502,totalTokens:4,validationFailure:"answer_schema");
        };
        await Seed();using var app=App(fake,OllamaTests.Settings);var owner=await Session();using var client=Authorized(app,owner.Token);
        var response=await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Stock","tr",Remember:true),Ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var answer=(await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;Assert.Equal("fallback",answer.ResponseKind);Assert.False(answer.IsMock);Assert.Equal(3,fake.Requests);
        var run=(await client.GetFromJsonAsync<AiExecutionStatus>("/api/ai/executions/"+answer.ExecutionId,Ct))!;Assert.Single(run.Tools);Assert.Equal(3,run.ProviderCalls);Assert.Equal(8,run.TotalTokens);
        var history=(await client.GetFromJsonAsync<AiConversationHistory>("/api/ai/conversations/"+answer.ConversationId,Ct))!;Assert.Equal("fallback",history.Messages.Last().ResponseKind);
    }
    [Fact]
    public async Task Chat_no_report_guidance_is_explicitly_deterministic()
    {
        var fake=new FakeModel();fake.Turns.Enqueue(Final());using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);
        var answer=(await (await Chat(client,"Hello")).Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;Assert.Equal("deterministic",answer.ResponseKind);Assert.Empty(answer.Sources);
    }
}
