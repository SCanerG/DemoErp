using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Api.Tests;
public sealed partial class AiTests
{
    [Fact]
    public async Task Authenticated_Responses_low_stock_flow_uses_real_reports_Turkish_and_correlated_metadata()
    {
        await Seed();var session=await Session();
        using var transport=new ResponsesTransport(i=>ResponsesTransport.Json(ResponsesTransport.Envelope(i==1
            ?[ResponsesTransport.Function(name:"get_low_stock_products",arguments:"{\"limit\":5}")]:[ResponsesTransport.Message()])));
        using var http=new HttpClient(transport);using var app=App(new OpenAiModelClient(new(){Enabled=true,ApiKey=Key},http));using var client=Authorized(app,session.Token);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before=(await db.Products.CountAsync(Ct),await db.Orders.CountAsync(Ct),await db.InventoryMovements.CountAsync(Ct),await db.AuditLogs.CountAsync(Ct));
        var result=await Chat(client,"Which products currently have low inventory?","tr");Assert.Equal(HttpStatusCode.OK,result.StatusCode);
        var answer=(await result.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;Assert.Equal("tr",answer.Language);Assert.False(answer.IsMock);
        Assert.Equal("get_low_stock_products",Assert.Single(answer.Sources).Name);Assert.NotNull(answer.ExecutionId);
        var run=await db.Set<AiExecution>().SingleAsync(r=>r.Id==answer.ExecutionId,Ct);Assert.Equal(answer.RequestId,run.CorrelationId);Assert.Equal("success",run.Outcome);Assert.Equal(30,run.TotalTokens);Assert.Equal(2,run.ProviderCalls);Assert.Equal(session.Id,run.UserId);
        var tool=await db.Set<AiToolAudit>().SingleAsync(t=>t.ExecutionId==run.Id,Ct);Assert.Equal("success",tool.Outcome);
        Assert.Contains("quantityOnHand",transport.Payloads[1]);Assert.Contains("function_call_output",transport.Payloads[1]);Assert.DoesNotContain(Key,transport.Payloads[1]);
        Assert.Equal(before,(await db.Products.CountAsync(Ct),await db.Orders.CountAsync(Ct),await db.InventoryMovements.CountAsync(Ct),await db.AuditLogs.CountAsync(Ct)));
    }
    [Fact]
    public async Task Retried_Responses_after_tool_output_do_not_execute_that_tool_twice()
    {
        var session=await Session();
        using var transport=new ResponsesTransport(i=>i==2?ResponsesTransport.Json("{\"error\":{\"code\":\"server_error\",\"message\":\"private-http-error\"}}",HttpStatusCode.ServiceUnavailable)
            :ResponsesTransport.Json(ResponsesTransport.Envelope(i==1?[ResponsesTransport.Function()]:[ResponsesTransport.Message()])));
        using var http=new HttpClient(transport);using var app=App(new OpenAiModelClient(new(){ApiKey=Key},http));using var client=Authorized(app,session.Token);
        var response=await Chat(client,"Stock","tr");Assert.Equal(HttpStatusCode.OK,response.StatusCode);var answer=(await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run=await db.Set<AiExecution>().SingleAsync(r=>r.Id==answer.ExecutionId,Ct);Assert.Equal(3,run.ProviderCalls);Assert.Equal(1,await db.Set<AiToolAudit>().CountAsync(t=>t.ExecutionId==run.Id,Ct));Assert.Single(answer.Sources);Assert.Equal(3,transport.Calls);
        Assert.Equal(transport.Payloads[1],transport.Payloads[2]);
    }
    [Fact]
    public async Task Responses_multiple_function_requests_are_executed_sequentially_once_and_audited()
    {
        var session=await Session();using var transport=new ResponsesTransport(i=>ResponsesTransport.Json(ResponsesTransport.Envelope(i==1
            ?[ResponsesTransport.Function(),ResponsesTransport.Function("call_2","get_pending_orders","{\"limit\":2}")]:[ResponsesTransport.Message()])));
        using var http=new HttpClient(transport);using var app=App(new OpenAiModelClient(new(){ApiKey=Key},http));using var client=Authorized(app,session.Token);
        var response=await Chat(client,"Stock and pending orders","tr");Assert.Equal(HttpStatusCode.OK,response.StatusCode);var answer=(await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;Assert.Equal(2,answer.Sources.Count);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(2,await db.Set<AiToolAudit>().CountAsync(t=>t.ExecutionId==answer.ExecutionId&&t.Outcome=="success",Ct));
    }
    [Fact]
    public async Task Invalid_Responses_tool_arguments_are_rejected_before_a_report_runs()
    {
        var session=await Session();using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(ResponsesTransport.Envelope([ResponsesTransport.Function(name:"get_low_stock_products",arguments:"{\"limit\":11}")])));
        using var http=new HttpClient(transport);using var app=App(new OpenAiModelClient(new(){ApiKey=Key},http));using var client=Authorized(app,session.Token);
        await Code(await Chat(client,"Stock","tr"),HttpStatusCode.BadRequest,"aiInvalidArguments");Assert.Equal(1,transport.Calls);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var run=await db.Set<AiExecution>().SingleAsync(r=>r.UserId==session.Id,Ct);Assert.Equal("aiInvalidArguments",run.Outcome);
        Assert.Equal("aiInvalidArguments",(await db.Set<AiToolAudit>().SingleAsync(t=>t.ExecutionId==run.Id,Ct)).Outcome);
    }
    [Fact]
    public async Task Unauthenticated_requests_never_reach_the_Responses_transport()
    {
        using var transport=new ResponsesTransport(_=>throw new InvalidOperationException("Must not reach provider"));using var http=new HttpClient(transport);using var app=App(new OpenAiModelClient(new(){ApiKey=Key},http));using var client=app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await Chat(client)).StatusCode);Assert.Equal(0,transport.Calls);
    }
    [Fact]
    public async Task Responses_refusal_has_safe_error_category_usage_and_no_persisted_answer()
    {
        var session=await Session();var refusal=new{type="message",id="msg_refused",role="assistant",status="completed",content=new[]{new{type="refusal",refusal="private-refusal"}}};
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json(ResponsesTransport.Envelope([refusal])));using var http=new HttpClient(transport);using var app=App(new OpenAiModelClient(new(){ApiKey=Key},http));using var client=Authorized(app,session.Token);
        await Code(await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Stock","tr",Remember:true),Ct),(HttpStatusCode)422,"aiRefused");
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var run=await db.Set<AiExecution>().SingleAsync(r=>r.UserId==session.Id,Ct);Assert.Equal("aiRefused",run.Outcome);Assert.Equal(15,run.TotalTokens);
        Assert.False(await db.Set<AiConversationMessage>().AnyAsync(m=>m.ConversationId==run.ConversationId,Ct));Assert.DoesNotContain(fixture.App.Logs.Messages,m=>m.Contains("private-refusal",StringComparison.Ordinal));
    }
    [Fact]
    public async Task Execution_metadata_is_owner_only_expiring_and_contains_no_prompt_or_key()
    {
        var fake=new FakeModel{Handler=(_,_,_)=>Task.FromResult(Final())};using var app=App(fake);var owner=await Session();var other=await Session();using var client=Authorized(app,owner.Token);using var stranger=Authorized(app,other.Token);
        var answer=(await (await Chat(client,"private-metadata-question")).Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;
        var path=$"/api/ai/executions/{answer.ExecutionId}";var response=await client.GetAsync(path,Ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
        var body=await response.Content.ReadAsStringAsync(Ct);Assert.DoesNotContain("private-metadata-question",body);Assert.DoesNotContain(Key,body);Assert.DoesNotContain("apiKey",body);
        var metadata=(await response.Content.ReadFromJsonAsync<AiExecutionStatus>(Ct))!;Assert.Equal(answer.RequestId,metadata.CorrelationId);
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync(path,Ct)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await app.CreateClient().GetAsync(path,Ct)).StatusCode);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Set<AiExecution>().Where(r=>r.Id==answer.ExecutionId).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.ExpiresAt,DateTimeOffset.UtcNow.AddMinutes(-1)),Ct);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(path,Ct)).StatusCode);
    }

}
