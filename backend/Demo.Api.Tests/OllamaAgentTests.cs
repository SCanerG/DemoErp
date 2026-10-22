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
    public async Task Inventory_agent_rejects_noninventory_tools_and_audits_denial()
    {
        var fake=new FakeModel(); fake.Turns.Enqueue(new(null,null,[Call("get_pending_orders","{\"limit\":5}")]));
        using var app=App(fake); var session=await Session(); using var client=Authorized(app,session.Token);
        await Code(await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Inventory","en",Agent:"inventory"),Ct),HttpStatusCode.BadRequest,"aiUnknownTool");
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run=await db.Set<AiExecution>().SingleAsync(r=>r.UserId==session.Id,Ct);
        Assert.Equal("aiUnknownTool",run.Outcome);
        Assert.Equal("aiUnknownTool",(await db.Set<AiToolAudit>().SingleAsync(a=>a.ExecutionId==run.Id,Ct)).Outcome);
    }
    [Fact]
    public async Task Order_agent_returns_review_only_draft_without_creating_orders()
    {
        using var app=App(new FakeModel());var session=await Session();using var client=Authorized(app,session.Token);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);
        var request=new OrderDraftRequest(Guid.NewGuid(),[new(Guid.NewGuid(),2)]);
        var response=await client.PostAsJsonAsync("/api/ai/order-drafts",request,Ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var draft=(await response.Content.ReadFromJsonAsync<OrderDraft>(Ct))!;Assert.False(draft.Persisted);Assert.True(draft.RequiresHumanReview);
        Assert.Equal("DraftOnly",draft.Status);
        var audit=(await client.GetFromJsonAsync<AiExecutionStatus>("/api/ai/executions/"+draft.ExecutionId,Ct))!;
        Assert.Equal("draftPrepared",audit.Outcome);Assert.Equal(0,audit.ProviderCalls);Assert.Equal(0,audit.TotalTokens);Assert.Equal(before,await db.Orders.CountAsync(Ct));
        await Code(await client.PostAsJsonAsync("/api/ai/order-drafts",request with { Lines=[new(Guid.Empty,0)] },Ct),HttpStatusCode.BadRequest,"aiInvalidRequest");
        using var anonymous=app.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync("/api/ai/order-drafts",request,Ct)).StatusCode);
    }
    [Fact]
    public async Task Live_Ollama_through_authenticated_DemoERP_inventory_and_audit()
    {
        if(Environment.GetEnvironmentVariable("OLLAMA_LIVE_SMOKE")!="true") Assert.Skip("Opt in with OLLAMA_LIVE_SMOKE=true; no model request made.");
        using var handler=new SocketsHttpHandler { AllowAutoRedirect=false, UseProxy=false };
        using var http=new HttpClient(handler) { Timeout=Timeout.InfiniteTimeSpan };
        using var app=App(new OllamaModelClient(OllamaTests.Settings,http),OllamaTests.Settings);
        var session=await Session();using var client=Authorized(app,session.Token); client.Timeout=TimeSpan.FromSeconds(150);
        await Seed(); // Synthetic low-stock row in disposable PostgreSQL only.
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);
        var response=await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Which products currently have low inventory?","tr",Agent:"inventory"),Ct);
        Assert.True(response.IsSuccessStatusCode,"DemoERP live request HTTP "+(int)response.StatusCode+": "+await response.Content.ReadAsStringAsync(Ct));
        var answer=(await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;Assert.False(answer.IsMock);
        Assert.Contains(answer.Sources,s=>s.Name=="get_low_stock_products");
        var run=(await client.GetFromJsonAsync<AiExecutionStatus>("/api/ai/executions/"+answer.ExecutionId,Ct))!;
        Assert.Equal("Ollama",run.Provider);Assert.Equal("qwen3:8b",run.Model);Assert.Equal("success",run.Outcome);Assert.True(run.TotalTokens>0);
        Assert.Equal(answer.RequestId,run.CorrelationId);Assert.Contains(run.Tools,t=>t.Name=="get_low_stock_products" && t.Outcome=="success");
        Assert.Equal(before,await db.Orders.CountAsync(Ct));
        Console.WriteLine("OLLAMA_LIVE_VERIFIED "+JsonSerializer.Serialize(run));
    }
}
