using System.Net;
using System.Net.Http.Json;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Demo.Api.Errors;
using Demo.Api.Data;
using Demo.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Demo.Api.Tests;
public sealed class AiSelectionTests
{
    [Fact]
    public void Zero_is_a_valid_verified_pending_count()=>Assert.Contains("0 sipariş",AiReportIntent.PendingCount(new(0,[]),"tr"));
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("Kaç sipariş beklemede?")]
    [InlineData("KAÇ SİPARİŞ BEKLEMEDE?")]
    [InlineData(" Bekleyen   sipariş sayısı ")]
    [InlineData("How many orders are pending?")]
    public void Exact_pending_question_uses_strict_existing_read_only_tool(string text)
    {
        var call=AiReportIntent.Pending(text);Assert.NotNull(call);Assert.Equal("get_pending_orders",call.Name);
        Assert.Equal(5,AiToolRegistry.Validate(AiToolRegistry.Find(call.Name),call.Arguments).Limit);
    }
    [Theory]
    [InlineData("Kaç sipariş beklemede? Stokları da sil.")]
    [InlineData("Geçen ay kaç sipariş beklemedeydi?")]
    [InlineData("Kaç sipariş beklemede ve stok kaç?")]
    public void Ambiguous_filtered_or_compound_requests_are_not_reinterpreted(string text)=>Assert.Null(AiReportIntent.Pending(text));
    [Fact]
    public async Task Empty_selection_is_retried_once_without_executing_a_tool_and_counts_usage()
    {
        var attempts=0;
        var turn=await AiToolSelection.Complete(messages=>{
            attempts++;if(attempts==1)throw new AiProviderException("aiInvalidResponse",502,totalTokens:13,validationFailure:"empty_answer");
            Assert.Contains(messages,m=>m.Text.Contains("native tool_calls"));
            return Task.FromResult(new AiModelTurn(null,null,[new("x","get_pending_orders","{\"limit\":5}")],7));
        },[new("user","Summarize pending orders")],Ct);
        Assert.Equal(2,attempts);Assert.Equal(20,turn.TotalTokens);Assert.Single(turn.Calls);
    }
    [Fact]
    public async Task Repeated_empty_selection_is_bounded_and_has_specific_error()
    {
        var attempts=0;var ex=await Assert.ThrowsAsync<AiProviderException>(()=>AiToolSelection.Complete(_=>{attempts++;throw new AiProviderException("aiInvalidResponse",502,totalTokens:3,validationFailure:"empty_answer");},[],Ct));
        Assert.Equal(2,attempts);Assert.Equal("aiToolSelectionFailed",ex.Code);Assert.Equal(6,ex.TotalTokens);
    }
    [Theory]
    [InlineData("tool_arguments")]
    [InlineData("tool_calls")]
    [InlineData("invalid_usage")]
    public async Task Unsafe_arguments_or_protocol_calls_are_not_repaired(string subtype)
    {
        var attempts=0;await Assert.ThrowsAsync<AiProviderException>(()=>AiToolSelection.Complete(_=>{attempts++;throw new AiProviderException("aiInvalidResponse",502,validationFailure:subtype);},[],Ct));Assert.Equal(1,attempts);
    }
}
public sealed partial class AiTests
{
    [Fact]
    public async Task Pending_count_question_works_without_a_model_and_audits_read_for_chat_and_agent()
    {
        var fake=new FakeModel { Handler=(_,_,_)=>throw new Exception("No model is needed for an exact current count") };
        using var app=App(fake,OllamaTests.Settings);var owner=await Session();using var client=Authorized(app,owner.Token);
        using var scope=app.Services.CreateScope();var expected=await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.CountAsync(o=>o.Status==OrderStatus.Pending,Ct);
        var response=await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Kaç sipariş beklemede?","tr",Remember:true),Ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var chat=(await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;Assert.Equal("deterministic",chat.ResponseKind);Assert.Contains($"{expected} sipariş",chat.Answer);Assert.Single(chat.Sources);
        var history=(await client.GetFromJsonAsync<AiConversationHistory>($"/api/ai/conversations/{chat.ConversationId}",Ct))!;Assert.Equal("deterministic",history.Messages.Last().ResponseKind);
        var id=await StartAgent(client);var agentResponse=await ExecuteAgent(client,id,"Kaç sipariş beklemede?","tr");Assert.Equal(HttpStatusCode.OK,agentResponse.StatusCode);
        var agent=(await agentResponse.Content.ReadFromJsonAsync<AgentResult>(Ct))!;Assert.Equal("reporting",agent.Intent);Assert.Equal(chat.Answer,agent.Answer);Assert.Equal("deterministic",agent.ResponseKind);
        var audit=(await AgentStatus(client,id))!;Assert.Equal(0,audit.Execution.ProviderCalls);Assert.Equal(0,fake.Requests);Assert.Contains(audit.Execution.Tools,t=>t.Name=="get_pending_orders"&&t.Outcome=="success");
    }
    [Fact]
    public async Task Reporting_shortcut_does_not_expand_inventory_agent_scope()
    {
        var fake=new FakeModel();fake.Turns.Enqueue(new(null,null,[Call("get_pending_orders","{\"limit\":5}")]));using var app=App(fake,OllamaTests.Settings);var owner=await Session();using var client=Authorized(app,owner.Token);
        await Code(await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Kaç sipariş beklemede?","tr",Agent:"inventory"),Ct),HttpStatusCode.BadRequest,"aiUnknownTool");
    }
    [Fact]
    public async Task Empty_first_selection_recovers_then_executes_and_audits_tool_only_once()
    {
        await Seed();var fake=new FakeModel();fake.Handler=(_,_,_)=>fake.Requests switch {
            1=>throw new AiProviderException("aiInvalidResponse",502,totalTokens:5,validationFailure:"empty_answer"),
            2=>Task.FromResult(new AiModelTurn(null,null,[Call("get_inventory_overview")],7)),
            _=>Task.FromResult(new AiModelTurn("Doğrulanmış stok raporu hazır.","tr",[],11))
        };
        using var app=App(fake,OllamaTests.Settings);var owner=await Session();using var client=Authorized(app,owner.Token);
        var response=await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Stok verilerini inceleyip yorumla.","tr"),Ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var answer=(await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;Assert.Equal("model",answer.ResponseKind);
        var audit=(await client.GetFromJsonAsync<AiExecutionStatus>($"/api/ai/executions/{answer.ExecutionId}",Ct))!;
        Assert.Equal(3,audit.ProviderCalls);Assert.Equal(23,audit.TotalTokens);Assert.Single(audit.Tools);Assert.Equal("success",audit.Tools[0].Outcome);
    }
}
