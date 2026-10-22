using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Demo.Api.Tests;
public sealed partial class AiTests
{
    private async Task<Guid> StartAgent(HttpClient client,string mode="auto") {
        var response=await client.PostAsJsonAsync("/api/ai/agents/runs",new AgentStartRequest(mode),Ct);Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));return json.RootElement.GetProperty("id").GetGuid();
    }
    private static Task<HttpResponseMessage> ExecuteAgent(HttpClient client,Guid id,string text="Analyze inventory",string language="en",string period="last3months")=>
        client.PostAsJsonAsync($"/api/ai/agents/runs/{id}/execute",new AgentInput(text,language,period),Ct);
    private static Task<AgentRunStatus?> AgentStatus(HttpClient client,Guid id)=>client.GetFromJsonAsync<AgentRunStatus>($"/api/ai/agents/runs/{id}",Ct);
    private async Task<(Guid Product,Guid Customer,string ProductName,string CustomerName)> SeedAgentData()
    {
        using var scope=fixture.App.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var suffix=Guid.NewGuid().ToString("N");
        var category=new Category{Name="Agent category"};var product=new Product{Name="AgentProduct-"+suffix,CategoryId=category.Id,Price=20,IsActive=true};var customer=new Customer{Name="AgentCustomer-"+suffix};
        db.AddRange(category,product,customer);await db.SaveChangesAsync(Ct);
        var balance=await db.WarehouseInventories.SingleAsync(i=>i.ProductId==product.Id,Ct);balance.QuantityOnHand=3;balance.MinimumStockLevel=10;
        db.Orders.Add(new Order{CustomerId=customer.Id,WarehouseId=balance.WarehouseId,OrderNumber="AGENT-"+suffix,Status=OrderStatus.Completed,
            OrderDate=DateTimeOffset.UtcNow.AddDays(-6),CompletedAt=DateTimeOffset.UtcNow.AddDays(-5),TotalAmount=120,
            Items=[new(){ProductId=product.Id,Quantity=6,UnitPrice=20,LineTotal=120}]});await db.SaveChangesAsync(Ct);
        return(product.Id,customer.Id,product.Name,customer.Name);
    }
    private static AiModelTurn RouteOrder(string? customer,string product,int? quantity=10)=>new(null,null,[new("plan","route_erp_request",JsonSerializer.Serialize(new {
        kind="order",period="last3months",customer,lines=new[]{new {product,quantity}} }))],15);
    [Fact]
    public async Task Agent_inventory_uses_actual_reports_deterministic_targets_and_audited_steps()
    {
        var seeded=await SeedAgentData();var fake=new FakeModel();fake.Turns.Enqueue(Final());using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);
        var id=await StartAgent(client,"inventory");var response=await ExecuteAgent(client,id);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=(await response.Content.ReadFromJsonAsync<AgentResult>(Ct))!;Assert.Equal("inventory",result.Intent);Assert.NotNull(result.Inventory);Assert.Null(result.Sales);
        Assert.True(result.Inventory.MatchingProducts>0);Assert.All(result.Inventory.Products,p=>{Assert.True(p.SuggestedQuantity is null or >=0);Assert.True(p.CurrentStock>=0);});
        Assert.Single(result.Sources);var status=(await AgentStatus(client,id))!;
        Assert.Equal(new[]{"orchestrator","inventory","summary"},status.Steps.Select(s=>s.Agent));Assert.All(status.Steps,s=>Assert.Equal("success",s.Outcome));
        Assert.Equal("success",status.Execution.Outcome);Assert.Equal(1,status.Execution.ProviderCalls);Assert.Contains(status.Execution.Tools,t=>t.Name==AgentTools.Inventory&&t.Outcome=="success");
        Assert.Equal(HttpStatusCode.Conflict,(await ExecuteAgent(client,id)).StatusCode);
    }
    [Theory]
    [InlineData("sales","Analyze sales",false)]
    [InlineData("auto","Analyze sales and current inventory",true)]
    public async Task Agent_sales_and_coordination_reuse_existing_reports_sequentially(string mode,string prompt,bool multi)
    {
        await SeedAgentData();var fake=new FakeModel();fake.Turns.Enqueue(Final());using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);
        var id=await StartAgent(client,mode);var response=await ExecuteAgent(client,id,prompt);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=(await response.Content.ReadFromJsonAsync<AgentResult>(Ct))!;Assert.NotNull(result.Sales);Assert.True(result.Sales.Summary.Current.Sales>=120);Assert.NotNull(result.Sales.Summary.Previous);
        Assert.Equal(multi,result.Inventory is not null);var run=(await AgentStatus(client,id))!;
        Assert.Equal(multi?4:3,run.Steps.Count);Assert.Equal(multi?3:2,run.Execution.Tools.Count);Assert.Equal(1,fake.Requests);
        Assert.Equal("get_sales_summary",run.Execution.Tools[0].Name);Assert.Equal("get_top_products",run.Execution.Tools[1].Name);
        Assert.True(run.Steps.Single(s=>s.Agent=="sales").CreatedAt<=run.Steps.Last().CreatedAt);
    }
    [Fact]
    public async Task Intelligent_draft_resolves_real_customer_price_and_warehouse_without_business_writes()
    {
        var seed=await SeedAgentData();var fake=new FakeModel();fake.Turns.Enqueue(RouteOrder(seed.CustomerName,seed.ProductName));fake.Turns.Enqueue(Final());
        using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before=(await db.Orders.CountAsync(Ct),await db.Products.CountAsync(Ct),await db.InventoryMovements.CountAsync(Ct),await db.AuditLogs.CountAsync(a=>a.EntityName!="AiOrderProposal",Ct));
        var id=await StartAgent(client,"order");var response=await ExecuteAgent(client,id,"Prepare an order draft");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=(await response.Content.ReadFromJsonAsync<AgentResult>(Ct))!;var draft=result.OrderDraft!;Assert.Equal(seed.Customer,draft.CustomerId);Assert.Empty(draft.Clarifications);
        var line=Assert.Single(draft.Lines);Assert.Equal(seed.Product,line.ProductId);Assert.Equal(20,line.UnitPrice);Assert.Equal(200,draft.Total);Assert.Equal(3,line.AvailableStock);Assert.Equal(7,line.Shortage);
        Assert.True(draft.NotSubmitted);Assert.False(draft.Persisted);Assert.True(draft.RequiresHumanReview);
        Assert.Equal(before,(await db.Orders.CountAsync(Ct),await db.Products.CountAsync(Ct),await db.InventoryMovements.CountAsync(Ct),await db.AuditLogs.CountAsync(a=>a.EntityName!="AiOrderProposal",Ct)));
        var run=(await AgentStatus(client,id))!;Assert.Equal(2,run.Execution.ProviderCalls);Assert.Equal("resolve_order_draft",Assert.Single(run.Execution.Tools).Name);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_requires_clarification_for_unknown_or_ambiguous_customers(bool ambiguous)
    {
        var seed=await SeedAgentData();var query="unknown-"+Guid.NewGuid().ToString("N");
        if(ambiguous){using var scope=fixture.App.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();db.Customers.AddRange(new Customer{Name=query+" A"},new Customer{Name=query+" B"});await db.SaveChangesAsync(Ct);}
        var fake=new FakeModel();fake.Turns.Enqueue(RouteOrder(query,seed.ProductName));using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);
        var id=await StartAgent(client,"order");var response=await ExecuteAgent(client,id,"Prepare draft");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var draft=(await response.Content.ReadFromJsonAsync<AgentResult>(Ct))!.OrderDraft!;Assert.Null(draft.Total);Assert.Null(draft.CustomerId);
        Assert.Contains(draft.Clarifications,c=>c.Reason==(ambiguous?"ambiguous":"notFound"));Assert.Equal(1,fake.Requests);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("quantity")]
    public async Task Draft_does_not_invent_product_identity_or_missing_quantities(string scenario)
    {
        var seed=await SeedAgentData();var query="unknown-product-"+Guid.NewGuid().ToString("N");
        if(scenario=="ambiguous") {using var scope=fixture.App.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var category=await db.Products.Where(p=>p.Id==seed.Product).Select(p=>p.CategoryId).SingleAsync(Ct);db.Products.AddRange(new Product{Name=query+" A",CategoryId=category,Price=1,IsActive=true},new Product{Name=query+" B",CategoryId=category,Price=2,IsActive=true});await db.SaveChangesAsync(Ct);}
        var fake=new FakeModel();fake.Turns.Enqueue(RouteOrder(seed.CustomerName,scenario=="quantity"?seed.ProductName:query,scenario=="quantity"?null:10));
        using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);var id=await StartAgent(client,"order");
        var result=(await (await ExecuteAgent(client,id,"Draft")).Content.ReadFromJsonAsync<AgentResult>(Ct))!;
        Assert.Null(result.OrderDraft!.Total);Assert.Empty(result.OrderDraft.Lines);Assert.NotEmpty(result.OrderDraft.Clarifications);
    }
    [Fact]
    public async Task Agent_run_history_is_owner_scoped_and_expiring()
    {
        var fake=new FakeModel();fake.Turns.Enqueue(Final());using var app=App(fake);var owner=await Session();var other=await Session();using var a=Authorized(app,owner.Token);using var b=Authorized(app,other.Token);
        var id=await StartAgent(a);Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync($"/api/ai/agents/runs/{id}",Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await ExecuteAgent(b,id)).StatusCode);
        Assert.DoesNotContain((await b.GetFromJsonAsync<List<AgentRunSummary>>("/api/ai/agents/runs",Ct))!,r=>r.Id==id);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Set<AiExecution>().Where(e=>e.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.ExpiresAt,DateTimeOffset.UtcNow.AddDays(-1)),Ct);
        Assert.Equal(HttpStatusCode.NotFound,(await a.GetAsync($"/api/ai/agents/runs/{id}",Ct)).StatusCode);Assert.Equal(0,fake.Requests);
    }
    [Fact]
    public async Task Arbitrary_planner_and_summary_tools_cannot_dispatch_application_code()
    {
        foreach(var planning in new[]{true,false}) {
            var fake=new FakeModel();fake.Turns.Enqueue(new(null,null,[Call("execute_sql")]));using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);
            var id=await StartAgent(client,planning?"auto":"inventory");await Code(await ExecuteAgent(client,id,planning?"Unrelated request":"Inventory"),HttpStatusCode.BadGateway,"aiInvalidResponse");
            var run=(await AgentStatus(client,id))!;Assert.Equal("aiInvalidResponse",run.Execution.Outcome);Assert.DoesNotContain(run.Execution.Tools,t=>t.Name=="execute_sql");
        }
    }
    [Fact]
    public async Task Agent_deadline_finalizes_step_and_run_metadata()
    {
        var fake=new FakeModel {Handler=async(_,_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Final();}};
        using var app=App(fake,new(){Enabled=true,ApiKey=Key,TimeoutSeconds=1,MaxRetries=0});var owner=await Session();using var client=Authorized(app,owner.Token);
        var id=await StartAgent(client,"inventory");await Code(await ExecuteAgent(client,id),HttpStatusCode.GatewayTimeout,"aiTimeout");
        var status=(await AgentStatus(client,id))!;Assert.Equal("timeout",status.Execution.Outcome);Assert.DoesNotContain(status.Steps,s=>s.Outcome=="running");
    }
    [Fact]
    public async Task Agent_cancellation_is_propagated_and_audited()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake=new FakeModel {Handler=async(_,_,ct)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,ct);return Final();}};
        using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);var id=await StartAgent(client,"inventory");
        using var cts=new CancellationTokenSource();var pending=client.PostAsJsonAsync($"/api/ai/agents/runs/{id}/execute",new AgentInput("Inventory","en"),cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10),Ct);cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await pending);
        AgentRunStatus? run=null;for(var i=0;i<30;i++){run=await AgentStatus(client,id);if(run!.Execution.Outcome!="running")break;await Task.Delay(50,Ct);}
        Assert.Equal("cancelled",run!.Execution.Outcome);Assert.DoesNotContain(run.Steps,s=>s.Outcome=="running");
    }
    [Fact]
    public async Task Revoked_identity_cannot_receive_intermediate_facts()
    {
        var owner=await Session();var fake=new FakeModel {Handler=async(_,_,ct)=>{
            using var scope=fixture.App.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Users.Where(u=>u.Id==owner.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.IsActive,false),ct);return Final();}};
        using var app=App(fake);using var client=Authorized(app,owner.Token);var id=await StartAgent(client,"inventory");
        Assert.Equal(HttpStatusCode.Unauthorized,(await ExecuteAgent(client,id)).StatusCode);
    }
    [Theory]
    [InlineData("/api/ai/agents/status")]
    [InlineData("/api/ai/agents/runs")]
    public async Task Agent_endpoints_require_authentication(string path)=>Assert.Equal(HttpStatusCode.Unauthorized,(await fixture.Client.GetAsync(path,Ct)).StatusCode);
}

public sealed class AgentContractTests
{
    [Fact]
    public async Task Mock_agent_explanation_does_not_claim_live_generation_or_unrelated_inventory_reads()
    {
        var result=await new DevelopmentAiModelClient().Complete([new("system","You explain verified READ-ONLY ERP facts. language=en"),new("user","Sales facts")],[],TestContext.Current.CancellationToken);
        Assert.Contains("No live language model",result.Answer);Assert.DoesNotContain("inventory report",result.Answer);Assert.Equal(0,result.TotalTokens);
    }
    [Theory]
    [InlineData("Hangi ürünlerin stoğu düşük?","inventory")]
    [InlineData("Analyze current stock","inventory")]
    [InlineData("Satış analizi","sales")]
    [InlineData("Son üç ayın satış ve stoklarını analiz et","collaboration")]
    [InlineData("Prepare an order draft","order")]
    public void Fixed_routing_is_deterministic(string text,string expected)=>Assert.Equal(expected,AgentRouting.Simple(text));
    [Fact]
    public void Unknown_intent_does_not_silently_choose_an_agent()=>Assert.Null(AgentRouting.Simple("Tell me a joke"));
    [Fact]
    public void Limits_and_deterministic_replenishment_are_enforced()
    {
        var limits=new AgentBudget(1,1,1);limits.Step();limits.Tool();limits.Model();Assert.Throws<BusinessException>(limits.Step);Assert.Throws<BusinessException>(limits.Tool);Assert.Throws<BusinessException>(limits.Model);
        var now=DateTimeOffset.UtcNow;var window=new AgentWindow(now.AddDays(-90),now);var row=new InventoryReportRow(Guid.NewGuid(),"Product","Cat",3,10,"Low",now);
        var result=InventoryAnalysisAgent.Calculate(row,90,window);Assert.Equal(1,result.DailyAverage);Assert.Equal(14,result.TargetStock);Assert.Equal(11,result.SuggestedQuantity);
        Assert.Null(InventoryAnalysisAgent.Calculate(row with {MinimumStockLevel=0},0,window).SuggestedQuantity);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"execute_sql\",\"period\":\"last30days\",\"customer\":null,\"lines\":[]}")]
    [InlineData("{\"kind\":\"order\",\"period\":\"last30days\",\"customer\":\"A\",\"lines\":[{\"product\":\"B\",\"quantity\":0}]}")]
    [InlineData("{\"kind\":\"order\",\"period\":\"last30days\",\"customer\":\"A\",\"lines\":[{\"product\":\"B\",\"quantity\":1,\"price\":0}]}")]
    [InlineData("{\"kind\":\"inventory\",\"kind\":\"sales\",\"period\":\"last30days\",\"customer\":null,\"lines\":[]}")]
    public void Invalid_or_write_capable_plans_are_rejected(string json)=>Assert.Throws<BusinessException>(()=>AgentPlanner.Parse(new(null,null,[new("1","route_erp_request",json)]),"auto"));
    [Fact]
    public void Calendar_windows_are_utc_and_do_not_invent_periods()
    {
        var now=new DateTimeOffset(2026,10,21,10,0,0,TimeSpan.Zero);var window=AgentRouting.Window("last3months",now);
        Assert.Equal(now.AddMonths(-3),window.Start);Assert.Equal(now,window.End);
        Assert.Equal(new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero),AgentRouting.Window("previousmonth",now).Start);
        Assert.Throws<BusinessException>(()=>AgentRouting.Window("unbounded",now));
    }
    [Fact]
    public async Task Ollama_health_probe_is_cached_and_does_not_generate_text()
    {
        using var transport=new ResponsesTransport(_=>ResponsesTransport.Json("{\"capabilities\":[\"completion\",\"tools\"]}"));using var http=new HttpClient(transport);
        var health=new AgentHealth(OllamaTests.Settings,http);Assert.Equal("ready",(await health.Read(TestContext.Current.CancellationToken)).Health);
        Assert.Equal("ready",(await health.Read(TestContext.Current.CancellationToken)).Health);Assert.Equal(1,transport.Calls);Assert.EndsWith("/api/show",transport.Endpoint!.ToString());Assert.Null(transport.Authorization);
    }
}
