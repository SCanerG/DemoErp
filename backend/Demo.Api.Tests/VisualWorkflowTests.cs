using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Demo.Api.AI.Workflows;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Demo.Api.Tests;

public sealed class VisualWorkflowTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private sealed class FakeModel(string answer="{\"template\":\"weeklySales\",\"dayOfWeek\":5,\"hourUtc\":9,\"minuteUtc\":0,\"threshold\":10}",bool unavailable=false):IAiModelClient
    {
        public Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages,IReadOnlyList<AiToolDefinition> tools,CancellationToken ct)=>unavailable?Task.FromException<AiModelTurn>(new AiProviderException("aiProviderUnavailable",503)):Task.FromResult(new AiModelTurn(answer,"tr",[],17,"mock"));
    }
    private WebApplicationFactory<Program> App(IAiModelClient? model=null)=>fixture.App.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
        s.RemoveAll<WorkflowOptions>();s.AddSingleton(new WorkflowOptions{Enabled=true,PollSeconds=1});
        foreach(var worker in s.Where(d=>d.ServiceType==typeof(IHostedService)&&d.ImplementationType==typeof(WorkflowWorker)).ToList())s.Remove(worker);
        s.RemoveAll<IAiModelClient>();s.AddScoped<IAiModelClient>(_=>model??new FakeModel());s.RemoveAll<AiAssistantOptions>();s.AddSingleton(new AiAssistantOptions{Enabled=true,Provider="Mock",Model="workflow-test",AllowDevelopmentMock=true});
    }));
    private static async Task<(HttpClient Client,Guid Id)> Account(WebApplicationFactory<Program> app,UserRole role=UserRole.Admin)
    {
        using var scope=app.Services.CreateScope();var auth=scope.ServiceProvider.GetRequiredService<AuthService>();var email=Guid.NewGuid()+"@example.com";await auth.Register(new("Workflow fixture",email,"WorkflowFixture123!"),Ct);
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var user=await db.Users.SingleAsync(u=>u.Email==email,Ct);user.Role=role;await db.SaveChangesAsync(Ct);var session=await auth.Login(new(email,"WorkflowFixture123!"),Ct);var client=app.CreateClient();client.DefaultRequestHeaders.Authorization=new("Bearer",session!.AccessToken);return(client,user.Id);
    }
    private static async Task<WorkflowDefinition> Create(HttpClient client,WorkflowGraph? graph=null)
    {
        var response=await client.PostAsJsonAsync("/api/workflows",new WorkflowSave("Synthetic workflow","Isolated test",0,graph??WorkflowRegistry.Template("criticalInventory")),Ct);Assert.Equal(HttpStatusCode.Created,response.StatusCode);return(await response.Content.ReadFromJsonAsync<WorkflowDefinition>(Ct))!;
    }
    private static async Task<WorkflowDefinition> Publish(HttpClient client,WorkflowDefinition row)
    {
        var response=await client.PostAsJsonAsync($"/api/workflows/{row.Id}/publish",new WorkflowCommand(row.Revision),Ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return(await response.Content.ReadFromJsonAsync<WorkflowDefinition>(Ct))!;
    }
    private static async Task<Guid> Queue(HttpClient client,Guid id,bool simulation=true,Guid? key=null)
    {
        var response=await client.PostAsJsonAsync($"/api/workflows/{id}/runs",new WorkflowRunRequest(key??Guid.NewGuid(),simulation),Ct);Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);return(await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }
    private static async Task Process(WebApplicationFactory<Program> app)
    {
        using var scope=app.Services.CreateScope();Assert.True(await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().ProcessNext(Ct));
    }
    private static async Task<JsonElement> Run(HttpClient client,Guid id)=>await client.GetFromJsonAsync<JsonElement>($"/api/workflows/runs/{id}",Ct);
    [Theory]
    [InlineData("blank")][InlineData("criticalInventory")][InlineData("weeklySales")][InlineData("overduePurchase")][InlineData("inventoryPolicy")]
    public void Registered_templates_are_valid_acyclic_graphs(string key)=>Assert.Equal(WorkflowRegistry.Template(key).Nodes.Count,WorkflowRegistry.Validate(WorkflowRegistry.Template(key)).Count);
    [Theory]
    [InlineData("cycle")][InlineData("unknown")][InlineData("orphan")][InlineData("duplicate")][InlineData("missingTarget")][InlineData("triggerInput")][InlineData("unapprovedWrite")][InlineData("oversized")][InlineData("retryWrite")][InlineData("badPolicy")]
    public void Invalid_or_escalating_graphs_are_rejected(string kind)
    {
        var g=WorkflowRegistry.Template("blank");
        switch(kind){case "cycle":g.Edges.Add(new("reverse","end","start"));break;case "unknown":g.Nodes[1]=g.Nodes[1]with{Type="sql.execute"};break;case "orphan":g.Nodes.Add(new("orphan","action.report",0,0,new()));break;case "duplicate":g.Nodes.Add(g.Nodes[0]);break;case "missingTarget":g.Edges[0]=g.Edges[0]with{Target="missing"};break;case "triggerInput":g.Nodes[1]=g.Nodes[1]with{Type="trigger.schedule"};break;case "unapprovedWrite":g.Nodes[1]=g.Nodes[1]with{Type="action.executeApproved"};break;case "oversized":g=g with{Nodes=Enumerable.Range(0,26).Select(i=>new WorkflowNode("node"+i,"action.report",0,0,new())).ToList()};break;case "retryWrite":g.Nodes.Insert(1,new("retry","control.retry",0,0,new()));g.Nodes.Insert(2,new("notify","action.notify",0,0,new()));g=g with{Edges=[new("a","start","retry"),new("b","retry","notify"),new("c","notify","end")]};break;case "badPolicy":g=g with{Policy=new(TimeoutSeconds:1000)};break;}
        Assert.Throws<BusinessException>(()=>WorkflowRegistry.Validate(g));
    }
    [Fact]
    public void Weekly_schedule_and_conditions_use_explicit_UTC_and_typed_values()
    {
        var now=new DateTimeOffset(2026,10,23,9,0,0,TimeSpan.Zero);Assert.Equal(now.AddDays(7),WorkflowRegistry.NextWeekly(new(DayOfWeek:5,HourUtc:9),now));
        Assert.Equal(now,WorkflowRegistry.NextWeekly(new(DayOfWeek:5,HourUtc:9),now.AddMinutes(-1)));
        var n=new WorkflowNode("test","condition.numeric",0,0,new(Field:"stock",Comparison:"lt",Number:10));Assert.True(WorkflowHandlers.Condition(n,new(Stock:4)).Decision);Assert.False(WorkflowHandlers.Condition(n,new()).Decision);
        Assert.False(WorkflowHandlers.Condition(n with{Type="condition.boolean",Config=new(Field:"approved",Expected:true)},new(Approved:false)).Decision);
    }
    [Fact]
    public async Task Draft_simulation_is_private_durable_idempotent_and_cannot_write_business_records()
    {
        await using var app=App();var user=await Account(app);var other=await Account(app);var row=await Create(user.Client);var key=Guid.NewGuid();var run=await Queue(user.Client,row.Id,true,key);Assert.Equal(run,await Queue(user.Client,row.Id,true,key));
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);var purchaseBefore=await db.PurchaseOrders.CountAsync(Ct);await Process(app);
        var detail=await Run(user.Client,run);Assert.Equal("success",detail.GetProperty("run").GetProperty("state").GetString());Assert.Equal("simulation",detail.GetProperty("run").GetProperty("mode").GetString());Assert.True(detail.GetProperty("version").GetInt32()<0);Assert.Equal(4,detail.GetProperty("nodes").GetArrayLength());
        Assert.All(detail.GetProperty("nodes").EnumerateArray(),n=>Assert.Equal("simulation",n.GetProperty("output").GetProperty("kind").GetString()));
        Assert.Equal(before,await db.Orders.CountAsync(Ct));Assert.Equal(purchaseBefore,await db.PurchaseOrders.CountAsync(Ct));Assert.False(await db.Set<WorkflowNotification>().AnyAsync(n=>n.RunId==run,Ct));
        Assert.Equal(HttpStatusCode.NotFound,(await other.Client.GetAsync($"/api/workflows/{row.Id}",Ct)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.Client.GetAsync($"/api/workflows/runs/{run}",Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.Client.PostAsJsonAsync($"/api/workflows/runs/{run}/cancel",new{},Ct)).StatusCode);
    }
    [Fact]
    public async Task Viewer_and_anonymous_cannot_edit_publish_or_enqueue_and_unknown_properties_are_rejected()
    {
        await using var app=App();var viewer=await Account(app,UserRole.Viewer);var admin=await Account(app);using var anon=app.CreateClient();var input=new WorkflowSave("Forbidden","",0,WorkflowRegistry.Template("blank"));
        Assert.Equal(HttpStatusCode.Forbidden,(await viewer.Client.PostAsJsonAsync("/api/workflows",input,Ct)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsJsonAsync("/api/workflows",input,Ct)).StatusCode);
        var row=await Create(admin.Client);Assert.Equal(HttpStatusCode.Forbidden,(await viewer.Client.PostAsJsonAsync($"/api/workflows/{row.Id}/publish",new WorkflowCommand(row.Revision),Ct)).StatusCode);
        var malicious=WorkflowModel.Write(input).Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"sql\":\"DROP TABLE Orders\"");using var payload=new StringContent(malicious,System.Text.Encoding.UTF8,"application/json");Assert.Equal(HttpStatusCode.BadRequest,(await admin.Client.PostAsync("/api/workflows",payload,Ct)).StatusCode);
    }
    [Fact]
    public async Task Published_runs_keep_original_version_and_revision_conflicts_are_rejected()
    {
        await using var app=App();var user=await Account(app);var row=await Publish(user.Client,await Create(user.Client,WorkflowRegistry.Template("blank")));var run=await Queue(user.Client,row.Id,false);
        var changed=new WorkflowSave("Changed","",row.Revision,WorkflowRegistry.Template("weeklySales"));var update=await user.Client.PutAsJsonAsync($"/api/workflows/{row.Id}",changed,Ct);var saved=(await update.Content.ReadFromJsonAsync<WorkflowDefinition>(Ct))!;
        Assert.Equal(HttpStatusCode.Conflict,(await user.Client.PutAsJsonAsync($"/api/workflows/{row.Id}",changed,Ct)).StatusCode);await Publish(user.Client,saved);await Process(app);
        var detail=await Run(user.Client,run);Assert.Equal(1,detail.GetProperty("version").GetInt32());Assert.Equal(2,detail.GetProperty("nodes").GetArrayLength());
    }
    [Fact]
    public async Task Live_deterministic_stock_trigger_creates_one_notification_and_read_reports_without_model()
    {
        await using var app=App(new FakeModel(unavailable:true));var user=await Account(app);
        using(var seed=app.Services.CreateScope()){var db=seed.ServiceProvider.GetRequiredService<AppDbContext>();db.Add(new Product{Name="Synthetic workflow material",IsActive=true,Price=12,Category=new(){Name="Workflow fixture"}});await db.SaveChangesAsync(Ct);}
        var row=await Publish(user.Client,await Create(user.Client));var run=await Queue(user.Client,row.Id,false);await Process(app);
        var detail=await Run(user.Client,run);Assert.Equal("success",detail.GetProperty("run").GetProperty("state").GetString());Assert.True(detail.GetProperty("nodes")[0].GetProperty("output").GetProperty("count").GetInt32()>0);
        var notifications=await user.Client.GetFromJsonAsync<JsonElement>("/api/workflows/notifications",Ct);var n=Assert.Single(notifications.EnumerateArray(),n=>n.GetProperty("runId").GetGuid()==run);Assert.Equal(HttpStatusCode.NoContent,(await user.Client.PostAsync($"/api/workflows/notifications/{n.GetProperty("id").GetGuid()}/read",null,Ct)).StatusCode);
        using var scope=app.Services.CreateScope();Assert.False(await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().ProcessNext(Ct));Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowNotification>().CountAsync(n=>n.RunId==run,Ct));
    }
    [Fact]
    public async Task Changed_owner_permissions_stop_queued_execution_and_prevent_reactivation()
    {
        await using var app=App();var user=await Account(app);var row=await Publish(user.Client,await Create(user.Client,WorkflowRegistry.Template("blank")));var run=await Queue(user.Client,row.Id,false);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Users.Where(u=>u.Id==user.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Role,UserRole.Viewer).SetProperty(u=>u.SecurityVersion,u=>u.SecurityVersion+1),Ct);}
        await Process(app);using var read=app.Services.CreateScope();var stored=await read.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowRun>().SingleAsync(r=>r.Id==run,Ct);Assert.Equal("failed",stored.State);Assert.Equal("workflowAuthorizationChanged",stored.Error);
    }
    [Fact]
    public async Task Scheduling_deduplicates_due_slot_and_pins_published_version()
    {
        await using var app=App();var user=await Account(app);var row=await Publish(user.Client,await Create(user.Client,WorkflowRegistry.Template("weeklySales")));
        Assert.Equal(HttpStatusCode.OK,(await user.Client.PostAsJsonAsync($"/api/workflows/{row.Id}/activation",new WorkflowCommand(row.Revision,true),Ct)).StatusCode);
        var due=DateTimeOffset.UtcNow.AddMinutes(-1);using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Set<WorkflowDefinition>().Where(w=>w.Id==row.Id).ExecuteUpdateAsync(s=>s.SetProperty(w=>w.NextRunAt,due),Ct);await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().Schedule(Ct);}
        using(var scope=app.Services.CreateScope()){await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().Schedule(Ct);Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowRun>().CountAsync(r=>r.WorkflowId==row.Id,Ct));}
        await Process(app);var runs=await user.Client.GetFromJsonAsync<JsonElement>($"/api/workflows/{row.Id}/runs",Ct);Assert.Equal("scheduled",runs[0].GetProperty("mode").GetString());Assert.Equal("success",runs[0].GetProperty("state").GetString());
    }
    [Fact]
    public async Task Cancelled_execution_cannot_emit_notifications_or_replay_approval()
    {
        await using var app=App();var user=await Account(app);var row=await Publish(user.Client,await Create(user.Client));var run=await Queue(user.Client,row.Id,false);
        Assert.Equal(HttpStatusCode.NoContent,(await user.Client.PostAsync($"/api/workflows/runs/{run}/cancel",null,Ct)).StatusCode);
        using var scope=app.Services.CreateScope();Assert.False(await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().ProcessNext(Ct));Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowNotification>().AnyAsync(n=>n.RunId==run,Ct));
        Assert.Equal(HttpStatusCode.Conflict,(await user.Client.PostAsJsonAsync($"/api/workflows/runs/{run}/approve",new ProposalApproval(Guid.NewGuid(),new string('A',64)),Ct)).StatusCode);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Model_drafting_creates_only_inactive_validated_draft_and_rejects_injection(bool malicious)
    {
        await using var app=App(new FakeModel(malicious?"{\"template\":\"sql.execute\",\"shell\":\"write\"}":"{\"template\":\"weeklySales\",\"dayOfWeek\":5,\"hourUtc\":9}"));var user=await Account(app);
        var response=await user.Client.PostAsJsonAsync("/api/workflows/ai-draft",new WorkflowDraftRequest("Every Friday analyze sales","en"),Ct);
        if(malicious){Assert.False(response.IsSuccessStatusCode);using var scope=app.Services.CreateScope();Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowDefinition>().AnyAsync(w=>w.OwnerId==user.Id,Ct));}
        else{Assert.Equal(HttpStatusCode.OK,response.StatusCode);var result=await response.Content.ReadFromJsonAsync<JsonElement>(Ct);Assert.False(result.GetProperty("enabled").GetBoolean());Assert.False(result.GetProperty("published").GetBoolean());var row=result.GetProperty("workflow");Assert.True(row.GetProperty("publishedVersion").ValueKind==JsonValueKind.Null);}
    }
    private static WorkflowGraph OrderGraph(Guid customer,Guid product)
    {
        var nodes=new List<WorkflowNode>{new("start","trigger.manual",0,0,new()),new("draft","agent.order",200,0,new(CustomerId:customer,ProductId:product,Quantity:2)),new("proposal","action.propose",400,0,new()),new("approval","action.requestApproval",600,0,new()),new("execute","action.executeApproved",800,0,new()),new("end","control.end",1000,0,new())};
        return new(1,nodes,nodes.Zip(nodes.Skip(1),(a,b)=>new WorkflowEdge("edge_"+a.Id,a.Id,b.Id)).ToList(),new());
    }
    private static async Task<(Guid Customer,Guid Product)> OrderFixture(WebApplicationFactory<Program> app)
    {
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var customer=new Customer{Name="Workflow customer "+Guid.NewGuid()};var product=new Product{Name="Workflow part "+Guid.NewGuid(),Price=12.5m,IsActive=true,Category=new(){Name="Workflow order fixture"}};db.AddRange(customer,product);await db.SaveChangesAsync(Ct);return(customer.Id,product.Id);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Order_workflow_requires_explicit_revalidated_idempotent_approval(bool stale)
    {
        await using var app=App();var user=await Account(app,UserRole.Manager);var data=await OrderFixture(app);var row=await Publish(user.Client,await Create(user.Client,OrderGraph(data.Customer,data.Product)));var run=await Queue(user.Client,row.Id,false);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);await Process(app);var detail=await Run(user.Client,run);Assert.Equal("waitingApproval",detail.GetProperty("run").GetProperty("state").GetString());Assert.Equal(before,await db.Orders.CountAsync(Ct));
        var pending=Assert.Single(detail.GetProperty("nodes").EnumerateArray(),n=>n.GetProperty("state").GetString()=="waitingApproval").GetProperty("output").GetProperty("data");
        var fingerprint=pending.GetProperty("fingerprint").GetString()!;var key=Guid.NewGuid();var input=new ProposalApproval(key,fingerprint);
        Assert.Equal(12.5m,pending.GetProperty("facts").GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
        if(stale){await db.Products.Where(p=>p.Id==data.Product).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Price,13m),Ct);Assert.Equal(HttpStatusCode.Conflict,(await user.Client.PostAsJsonAsync($"/api/workflows/runs/{run}/approve",input,Ct)).StatusCode);Assert.Equal(before,await db.Orders.CountAsync(Ct));return;}
        var approval=await user.Client.PostAsJsonAsync($"/api/workflows/runs/{run}/approve",input,Ct);Assert.Equal(HttpStatusCode.OK,approval.StatusCode);var result=(await approval.Content.ReadFromJsonAsync<ProposalView>(Ct))!;Assert.True(result.ResultVerified);Assert.Equal("Pending",result.ResultStatus);Assert.Equal(before+1,await db.Orders.CountAsync(Ct));
        await Process(app);Assert.Equal("success",(await Run(user.Client,run)).GetProperty("run").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK,(await user.Client.PostAsJsonAsync($"/api/workflows/runs/{run}/approve",input,Ct)).StatusCode);Assert.Equal(before+1,await db.Orders.CountAsync(Ct));Assert.Equal(HttpStatusCode.Conflict,(await user.Client.PostAsJsonAsync($"/api/workflows/runs/{run}/approve",new ProposalApproval(Guid.NewGuid(),fingerprint),Ct)).StatusCode);
        Assert.Equal(0,await db.WarehouseInventories.Where(i=>i.ProductId==data.Product).SumAsync(i=>i.QuantityOnHand,Ct));
    }
    [Fact]
    public async Task Simulated_order_approval_uses_only_fixtures_and_cancelled_real_proposal_cannot_execute()
    {
        await using var app=App();var user=await Account(app);var data=await OrderFixture(app);var row=await Publish(user.Client,await Create(user.Client,OrderGraph(data.Customer,data.Product)));var simulated=await Queue(user.Client,row.Id);await Process(app);Assert.Equal("success",(await Run(user.Client,simulated)).GetProperty("run").GetProperty("state").GetString());
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.False(await db.Set<AiOrderProposal>().AnyAsync(p=>p.UserId==user.Id,Ct));var before=await db.Orders.CountAsync(Ct);
        var live=await Queue(user.Client,row.Id,false);await Process(app);var detail=await Run(user.Client,live);var node=Assert.Single(detail.GetProperty("nodes").EnumerateArray(),n=>n.GetProperty("state").GetString()=="waitingApproval");var pending=node.GetProperty("output").GetProperty("data");
        Assert.Equal(HttpStatusCode.NoContent,(await user.Client.PostAsync($"/api/workflows/runs/{live}/cancel",null,Ct)).StatusCode);Assert.Equal("cancelled",await db.Set<AiOrderProposal>().Where(p=>p.Id==pending.GetProperty("id").GetGuid()).Select(p=>p.State).SingleAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict,(await user.Client.PostAsJsonAsync($"/api/workflows/runs/{live}/approve",new ProposalApproval(Guid.NewGuid(),pending.GetProperty("fingerprint").GetString()!),Ct)).StatusCode);Assert.Equal(before,await db.Orders.CountAsync(Ct));
    }
    private sealed class Faults{public int Calls;public TaskCompletionSource Started{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public string Mode="retry";}
    private sealed class FaultyHandlers(WorkflowHandlers inner,Faults faults):IWorkflowHandlers
    {
        public Task<WorkflowValue> Trigger(WorkflowNode n,System.Security.Claims.ClaimsPrincipal u,CancellationToken ct)=>inner.Trigger(n,u,ct);
        public async Task<WorkflowValue> Execute(WorkflowNode n,WorkflowValue v,WorkflowRun r,System.Security.Claims.ClaimsPrincipal u,CancellationToken ct)
        {
            if(n.Type=="action.report"){
                var call=Interlocked.Increment(ref faults.Calls);faults.Started.TrySetResult();
                if(faults.Mode=="timeout")await Task.Delay(Timeout.InfiniteTimeSpan,ct);
                if(faults.Mode=="cancel")await faults.Release.Task.WaitAsync(ct);
                if(faults.Mode=="retry"&&call==1)throw new BusinessException("syntheticTransient",503);
            }
            return await inner.Execute(n,v,r,u,ct);
        }
    }
    private WebApplicationFactory<Program> FaultApp(Faults faults)=>App().WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.RemoveAll<IWorkflowHandlers>();s.AddScoped<IWorkflowHandlers>(sp=>new FaultyHandlers(sp.GetRequiredService<WorkflowHandlers>(),faults));}));
    private static WorkflowGraph ReportGraph()=>new(1,[new("start","trigger.manual",0,0,new()),new("report","action.report",200,0,new()),new("notify","action.notify",400,0,new()),new("end","control.end",600,0,new())],[new("a","start","report"),new("b","report","notify"),new("c","notify","end")],new(TimeoutSeconds:10));
    [Fact]
    public async Task Read_retries_are_bounded_and_recorded_without_repeating_notification()
    {
        var faults=new Faults();await using var app=FaultApp(faults);var user=await Account(app);var row=await Publish(user.Client,await Create(user.Client,ReportGraph()));var run=await Queue(user.Client,row.Id,false);await Process(app);var detail=await Run(user.Client,run);Assert.Equal("success",detail.GetProperty("run").GetProperty("state").GetString());Assert.Equal(2,Assert.Single(detail.GetProperty("nodes").EnumerateArray(),n=>n.GetProperty("nodeId").GetString()=="report").GetProperty("attempts").GetInt32());
        using var scope=app.Services.CreateScope();Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowNotification>().CountAsync(n=>n.RunId==run,Ct));
    }
    [Fact]
    public async Task Cancellation_during_read_is_fenced_before_notification_commit()
    {
        var faults=new Faults{Mode="cancel"};await using var app=FaultApp(faults);var user=await Account(app);var row=await Publish(user.Client,await Create(user.Client,ReportGraph()));var run=await Queue(user.Client,row.Id,false);var working=Task.Run(()=>Process(app),Ct);await faults.Started.Task.WaitAsync(TimeSpan.FromSeconds(10),Ct);
        Assert.Equal(HttpStatusCode.NoContent,(await user.Client.PostAsync($"/api/workflows/runs/{run}/cancel",null,Ct)).StatusCode);faults.Release.TrySetResult();await working;Assert.Equal("cancelled",(await Run(user.Client,run)).GetProperty("run").GetProperty("state").GetString());using var scope=app.Services.CreateScope();Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowNotification>().AnyAsync(n=>n.RunId==run,Ct));
    }
    [Fact]
    public async Task Execution_deadline_cancels_stalled_read_and_records_timeout_without_business_writes()
    {
        var faults=new Faults{Mode="timeout"};await using var app=FaultApp(faults);var user=await Account(app);var row=await Publish(user.Client,await Create(user.Client,ReportGraph()));var run=await Queue(user.Client,row.Id,false);await Process(app);var detail=await Run(user.Client,run);Assert.Equal("failed",detail.GetProperty("run").GetProperty("state").GetString());Assert.Equal("workflowTimeout",detail.GetProperty("run").GetProperty("error").GetString());
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Restart_recovery_resumes_read_checkpoints_but_never_replays_uncertain_side_effect(bool unsafeStep)
    {
        await using var app=App();var user=await Account(app);var graph=ReportGraph();if(unsafeStep)graph.Nodes[1]=graph.Nodes[1]with{Type="action.notify"};var row=await Publish(user.Client,await Create(user.Client,graph));var run=await Queue(user.Client,row.Id,false);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Set<WorkflowRun>().Where(r=>r.Id==run).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.State,"running").SetProperty(r=>r.Lease,"expired").SetProperty(r=>r.LeaseUntil,DateTimeOffset.UtcNow.AddMinutes(-1)).SetProperty(r=>r.Attempts,1),Ct);db.AddRange(new WorkflowNodeRun{RunId=run,NodeId="start",Type="trigger.manual",State="success",Output=WorkflowModel.Write(new WorkflowValue(Evidence:true)),Attempts=1},new WorkflowNodeRun{RunId=run,NodeId="report",Type=unsafeStep?"action.notify":"action.report",State="running",Output="{}",Attempts=1});await db.SaveChangesAsync(Ct);}
        await Process(app);var detail=await Run(user.Client,run);Assert.Equal(unsafeStep?"failed":"success",detail.GetProperty("run").GetProperty("state").GetString());if(unsafeStep)Assert.Equal("workflowInterruptedReview",detail.GetProperty("run").GetProperty("error").GetString());
    }
    [Fact]
    public async Task Live_Ollama_creates_validated_inactive_workflow_draft_without_executing_or_writing_orders()
    {
        if(Environment.GetEnvironmentVariable("VISUAL_WORKFLOW_LIVE_SMOKE")!="true")Assert.Skip("Opt in with VISUAL_WORKFLOW_LIVE_SMOKE=true; no local model request made.");
        using var handler=new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false};using var http=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};var settings=OllamaTests.Settings;
        await using var app=App().WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.RemoveAll<AiAssistantOptions>();s.AddSingleton(settings);s.RemoveAll<IAiModelClient>();s.AddScoped<IAiModelClient>(sp=>new Demo.Api.AI.Quality.ObservedModelClient(new OllamaModelClient(settings,http),sp.GetRequiredService<Demo.Api.AI.Quality.QualityTelemetry>(),settings));}));
        var user=await Account(app);using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);user.Client.Timeout=TimeSpan.FromSeconds(150);
        var response=await user.Client.PostAsJsonAsync("/api/workflows/ai-draft",new WorkflowDraftRequest("Every Friday at 09:00 UTC prepare a weekly sales report.","en"),Ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var result=await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Ollama",result.GetProperty("provider").GetString());Assert.Equal("model",result.GetProperty("origin").GetString());Assert.True(result.GetProperty("reportedTokens").GetInt32()>0);var row=result.GetProperty("workflow");Assert.False(row.GetProperty("enabled").GetBoolean());Assert.Equal(JsonValueKind.Null,row.GetProperty("publishedVersion").ValueKind);Assert.Equal(before,await db.Orders.CountAsync(Ct));
        Assert.False(await db.Set<WorkflowRun>().AnyAsync(r=>r.WorkflowId==row.GetProperty("id").GetGuid(),Ct));Console.WriteLine("OLLAMA_WORKFLOW_DRAFT_VERIFIED "+WorkflowModel.Write(new{workflowId=row.GetProperty("id").GetGuid(),provider="Ollama",model=settings.Model,totalTokens=result.GetProperty("reportedTokens").GetInt32(),activated=false,ordersUnchanged=true}));
    }
    private sealed class SelectedModel(string name,string args):IAiModelClient
    {
        public Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages,IReadOnlyList<AiToolDefinition> tools,CancellationToken ct)=>Task.FromResult(new AiModelTurn(null,null,[new("selection",name,args)],10));
    }
    [Theory]
    [InlineData("unknown")][InlineData("duplicate")][InlineData("stringNumber")][InlineData("missing")]
    public async Task Draft_selector_rejects_unregistered_ambiguous_and_malformed_tool_arguments(string kind)
    {
        var args="{\"template\":\"weeklySales\",\"dayOfWeek\":5,\"hourUtc\":9,\"minuteUtc\":0,\"threshold\":10}";
        if(kind=="duplicate")args=args.Replace("\"template\":\"weeklySales\"","\"template\":\"weeklySales\",\"Template\":\"inventoryPolicy\"");
        if(kind=="stringNumber")args=args.Replace("\"dayOfWeek\":5","\"dayOfWeek\":\"5\"");
        if(kind=="missing")args="{\"template\":\"weeklySales\"}";
        await using var app=App(new SelectedModel(kind=="unknown"?"create_order":WorkflowDrafting.Selector.Name,args));var user=await Account(app);
        Assert.Equal(HttpStatusCode.BadGateway,(await user.Client.PostAsJsonAsync("/api/workflows/ai-draft",new WorkflowDraftRequest("Prepare weekly sales report","en"),Ct)).StatusCode);
        using var scope=app.Services.CreateScope();Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<WorkflowDefinition>().AnyAsync(w=>w.OwnerId==user.Id,Ct));
    }
}
