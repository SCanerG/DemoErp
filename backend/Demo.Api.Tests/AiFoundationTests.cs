using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.Data;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Api.Tests;
public sealed partial class AiTests
{
    [Fact]
    public async Task Opt_in_history_is_owner_scoped_bounded_and_can_be_deleted()
    {
        var fake = new FakeModel { Handler = (_, _, _) => Task.FromResult(Final()) };
        using var app = App(fake); var owner = await Session(); var other = await Session();
        using var client = Authorized(app, owner.Token); using var stranger = Authorized(app, other.Token);
        var first = await client.PostAsJsonAsync("/api/ai/chat", new AiChatRequest("Private remembered question", "en", Remember: true), Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        var result = (await first.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!; Assert.NotNull(result.ConversationId); Assert.NotNull(result.ExecutionId);
        var id = result.ConversationId.Value;
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/ai/conversations/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/ai/conversations/{id}", Ct)).StatusCode);
        var lists = await stranger.GetFromJsonAsync<List<AiConversationSummary>>("/api/ai/conversations", Ct); Assert.DoesNotContain(lists!, c => c.Id == id);
        await Code(await stranger.PostAsJsonAsync("/api/ai/chat", new AiChatRequest("Continue", "en", id, true), Ct), HttpStatusCode.NotFound, "aiConversationNotFound");
        var second = await client.PostAsJsonAsync("/api/ai/chat", new AiChatRequest("Follow up question", "en", id, true), Ct); Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains(fake.Seen, m => m.Role == "user" && m.Text == "Private remembered question");
        Assert.Equal(2, fake.Seen.Count(m => m.Role == "user"));
        var history = await client.GetFromJsonAsync<AiConversationHistory>($"/api/ai/conversations/{id}", Ct); Assert.Equal(4, history!.Messages.Count);
        Assert.DoesNotContain(fixture.App.Logs.Messages, m => m.Contains("Private remembered question", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/ai/conversations/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ai/conversations/{id}", Ct)).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<AiConversationMessage>().AnyAsync(m => m.ConversationId == id, Ct));
        var run = await db.Set<AiExecution>().SingleAsync(r => r.Id == result.ExecutionId, Ct); Assert.Null(run.ConversationId); Assert.Equal("success", run.Outcome); Assert.Null(run.EstimatedCost);
    }
    [Fact]
    public async Task Default_stateless_requests_store_metadata_but_not_prompt_or_answer()
    {
        var fake = new FakeModel(); fake.Turns.Enqueue(new(null, null, [Call("get_inventory_overview")])); fake.Turns.Enqueue(Final());
        using var app = App(fake); var session = await Session(); using var client = Authorized(app, session.Token);
        var response = await Chat(client, "Private stateless prompt"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct))!; Assert.Null(result.ConversationId);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<AiConversation>().AnyAsync(c => c.UserId == session.Id, Ct));
        var run = await db.Set<AiExecution>().SingleAsync(r => r.Id == result.ExecutionId, Ct); Assert.Equal("success", run.Outcome); Assert.Equal(2, run.ProviderCalls);
        var audit = Assert.Single(await db.Set<AiToolAudit>().Where(t => t.ExecutionId == run.Id).ToListAsync(Ct)); Assert.Equal("success", audit.Outcome); Assert.Equal("get_inventory_overview", audit.ToolName);
        Assert.DoesNotContain("Private stateless prompt", JsonSerializer.Serialize(run)); Assert.DoesNotContain(Key, JsonSerializer.Serialize(audit));
    }
    [Theory]
    [InlineData("execute_sql")]
    [InlineData("delete_users")]
    public async Task Denied_tool_requests_leave_safe_durable_audit_and_no_history(string name)
    {
        var fake = new FakeModel(); fake.Turns.Enqueue(new(null, null, [Call(name)])); using var app = App(fake);
        var session = await Session(); using var client = Authorized(app, session.Token);
        await Code(await client.PostAsJsonAsync("/api/ai/chat", new AiChatRequest("Try an unsupported action", "en", Remember:true), Ct), HttpStatusCode.BadRequest, "aiUnknownTool");
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.Set<AiExecution>().SingleAsync(r => r.UserId == session.Id, Ct); Assert.Equal("aiUnknownTool", run.Outcome);
        var audit = await db.Set<AiToolAudit>().SingleAsync(t => t.ExecutionId == run.Id, Ct); Assert.Equal("unregistered", audit.ToolName); Assert.Equal("aiUnknownTool", audit.Outcome);
        Assert.False(await db.Set<AiConversationMessage>().AnyAsync(m => m.ConversationId == run.ConversationId, Ct));
    }
    [Fact]
    public async Task Expired_history_is_inaccessible_and_cleanup_cascades_messages_and_tool_metadata()
    {
        var fake = new FakeModel(); fake.Turns.Enqueue(new(null, null, [Call("get_inventory_overview")])); fake.Turns.Enqueue(Final());
        using var app=App(fake); var session=await Session(); using var client=Authorized(app,session.Token);
        var result=(await (await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Remember", "en",Remember:true),Ct)).Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;
        using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<AiConversation>().Where(c=>c.Id==result.ConversationId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.ExpiresAt,DateTimeOffset.UtcNow.AddDays(-1)),Ct);
        await db.Set<AiExecution>().Where(c=>c.Id==result.ExecutionId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.ExpiresAt,DateTimeOffset.UtcNow.AddDays(-1)),Ct);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/ai/conversations/{result.ConversationId}",Ct)).StatusCode);
        await AiRetentionWorker.Purge(db,DateTimeOffset.UtcNow,Ct);
        Assert.False(await db.Set<AiConversationMessage>().AnyAsync(c=>c.ConversationId==result.ConversationId,Ct));
        Assert.False(await db.Set<AiToolAudit>().AnyAsync(c=>c.ExecutionId==result.ExecutionId,Ct));
    }
    [Fact]
    public async Task Conversation_revision_conflict_does_not_append_duplicate_messages()
    {
        var fake=new FakeModel { Handler=(_,_,_)=>Task.FromResult(Final()) }; using var app=App(fake);
        var session=await Session(); using var client=Authorized(app,session.Token);
        var result=(await (await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Remember", "en",Remember:true),Ct)).Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;
        fake.Handler=async (_,_,_)=>{
            using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Set<AiConversation>().Where(c=>c.Id==result.ConversationId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.Revision,c=>c.Revision+1),Ct); return Final();
        };
        await Code(await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Conflicting followup", "en",result.ConversationId,true),Ct),HttpStatusCode.Conflict,"aiConversationConflict");
        var history=await client.GetFromJsonAsync<AiConversationHistory>($"/api/ai/conversations/{result.ConversationId}",Ct); Assert.Equal(2,history!.Messages.Count);
    }
    [Fact]
    public async Task Persistent_conversation_limit_is_checked_before_provider_access()
    {
        var fake=new FakeModel {Handler=(_,_,_)=>Task.FromResult(Final())};using var app=App(fake);var session=await Session();using var client=Authorized(app,session.Token);
        var result=(await (await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Remember", "en",Remember:true),Ct)).Content.ReadFromJsonAsync<AiChatResponse>(Ct))!;
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<AiConversation>().Where(c=>c.Id==result.ConversationId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.Revision,10),Ct);
        await Code(await client.PostAsJsonAsync("/api/ai/chat",new AiChatRequest("Continue", "en",result.ConversationId,true),Ct),HttpStatusCode.Conflict,"aiConversationLimit");Assert.Equal(1,fake.Requests);
    }
    [Theory]
    [InlineData("/api/ai/conversations")]
    [InlineData("/api/ai/conversations/00000000-0000-0000-0000-000000000001")]
    public async Task Conversation_endpoints_enforce_authentication(string path)
    {Assert.Equal(HttpStatusCode.Unauthorized,(await fixture.Client.GetAsync(path,Ct)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await fixture.Client.DeleteAsync((path.EndsWith("conversations",StringComparison.Ordinal)?path+"/00000000-0000-0000-0000-000000000001":path),Ct)).StatusCode);}
    [Fact]
    public async Task Known_secret_input_is_rejected_before_provider_or_history()
    {
        var fake=new FakeModel();using var app=App(fake);var session=await Session();using var client=Authorized(app,session.Token);
        await Code(await Chat(client,Key),HttpStatusCode.BadRequest,"aiSensitiveInput");Assert.Equal(0,fake.Requests);
    }
}

public sealed class AiFoundationUnitTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"answer\":\"a\",\"language\":\"en\",\"extra\":1}")]
    [InlineData("{\"answer\":\"a\",\"answer\":\"b\",\"language\":\"en\"}")]
    [InlineData("{\"answer\":\"a\",\"language\":\"de\"}")]
    [InlineData("{\"answer\":1,\"language\":\"en\"}")]
    [InlineData("{\"answer\":\"\",\"language\":\"en\"}")]
    public void Final_output_schema_is_validated_at_runtime(string json)
    {Assert.Equal("aiInvalidResponse",Assert.Throws<BusinessException>(()=>AiOutputValidator.Parse(json)).Code);}
    [Fact]
    public void Valid_schema_and_readonly_policy_are_provider_independent()
    {
        Assert.Equal(("Ready","en"),AiOutputValidator.Parse("{\"answer\":\"Ready\",\"language\":\"en\"}"));
        var policy=new AiExecutionPolicy();policy.Demand(AiToolRegistry.All[0]);
        Assert.Equal("aiReadOnly",Assert.Throws<BusinessException>(()=>policy.Demand(AiToolRegistry.All[0] with {Access=AiToolAccess.RequiresHumanApproval})).Code);
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,true)]
    public void Mock_provider_is_configured_only_in_development(bool development,bool expected)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"AiAssistant:Provider","Mock"},{"AiAssistant:Enabled","true"}}).Build();
        Assert.Equal(expected,AiAssistantOptions.Read(config,development).Configured);
    }
    private sealed class Flaky : IAiModelClient
    {
        public int Calls {get;private set;}
        public string Code {get;set;}="aiProviderUnavailable";
        public Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages,IReadOnlyList<AiToolDefinition> tools,CancellationToken ct)
        {Calls++;throw new BusinessException(Code,503);}
    }
    [Theory]
    [InlineData("aiProviderUnavailable",3)]
    [InlineData("aiProviderRateLimit",3)]
    [InlineData("aiInvalidResponse",1)]
    public async Task Retries_are_bounded_and_never_retry_invalid_outputs(string code,int expected)
    {
        var model=new Flaky {Code=code}; var attempts=0;
        await Assert.ThrowsAsync<BusinessException>(()=>AiProviderRunner.Complete(model,[],[],new(){MaxRetries=2},()=>attempts++,Ct));
        Assert.Equal(expected,model.Calls);Assert.Equal(expected,attempts);
    }
    [Fact]
    public async Task Cancellation_prevents_retry_attempts()
    {
        var model=new Flaky();using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>AiProviderRunner.Complete(model,[],[],new(),()=>{},cancel.Token));Assert.Equal(0,model.Calls);
    }
    [Fact]
    public void Tool_results_reject_wrong_type_or_secret_contamination()
    {
        var tool=AiToolRegistry.Find("get_inventory_overview");
        Assert.Throws<BusinessException>(()=>AiOutputValidator.ToolPayload(new(tool.Name,DateTimeOffset.UtcNow,null,null,"Current","USD",true,"not-a-typed-result"),tool,"secret"));
        var product=AiToolRegistry.Find("get_low_stock_products");
        Assert.Throws<BusinessException>(()=>AiOutputValidator.ToolPayload(new(product.Name,DateTimeOffset.UtcNow,null,null,"Current","USD",true,new List<AiStock>{new("known-secret",1,2,"Low")}),product,"known-secret"));
    }
}
