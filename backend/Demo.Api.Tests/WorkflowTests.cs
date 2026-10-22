using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
namespace Demo.Api.Tests;

public sealed partial class AiTests
{
    private static Task<HttpResponseMessage> Approve(HttpClient client,ProposalView proposal,Guid? key=null,string? hash=null) =>
        client.PostAsJsonAsync($"/api/ai/agents/proposals/{proposal.Id}/approve",new ProposalApproval(key??Guid.NewGuid(),hash??proposal.Fingerprint),Ct);
    private async Task<ProposalView> SalesProposal(HttpClient client,FakeModel fake) {
        var seed=await SeedAgentData();fake.Turns.Enqueue(RouteOrder(seed.CustomerName,seed.ProductName));fake.Turns.Enqueue(Final());
        var run=await StartAgent(client,"order");var response=await ExecuteAgent(client,run,"Prepare an order draft");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<AgentResult>(Ct))!.Proposals!);
    }
    [Theory]
    [InlineData(UserRole.Admin)] [InlineData(UserRole.Manager)]
    public async Task Workflow_approval_persists_exact_pending_order_and_is_idempotent(UserRole role) {
        var fake=new FakeModel();using var app=App(fake);var user=await Session(role);using var client=Authorized(app,user.Token);
        var proposal=await SalesProposal(client,fake);var key=Guid.NewGuid();
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);
        var response=await Approve(client,proposal,key);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var saved=(await response.Content.ReadFromJsonAsync<ProposalView>(Ct))!;
        Assert.Equal("approved",saved.State);Assert.Equal("Pending",saved.ResultStatus);Assert.NotNull(saved.ResultId);
        var order=await db.Orders.AsNoTracking().Include(o=>o.Items).SingleAsync(o=>o.Id==saved.ResultId,Ct);
        Assert.Equal(proposal.Facts.CounterpartyId,order.CustomerId);Assert.Equal(proposal.Facts.Lines.Sum(l=>l.Quantity*l.UnitPrice),order.TotalAmount);
        Assert.Equal(10,Assert.Single(order.Items).Quantity);Assert.Equal(before+1,await db.Orders.CountAsync(Ct));
        Assert.Equal(proposal.Facts.Lines[0].Stock,await db.WarehouseInventories.Where(i=>i.ProductId==proposal.Facts.Lines[0].ProductId&&i.WarehouseId==proposal.Facts.WarehouseId).SumAsync(i=>(long)i.QuantityOnHand,Ct));
        Assert.Contains(await db.AuditLogs.AsNoTracking().Where(a=>a.EntityId==order.Id).ToListAsync(Ct),a=>a.UserId==user.Id&&a.Action=="Create");
        var retry=(await (await Approve(client,proposal,key)).Content.ReadFromJsonAsync<ProposalView>(Ct))!;Assert.Equal(saved.ResultId,retry.ResultId);
        await Code(await Approve(client,proposal),HttpStatusCode.Conflict,"aiProposalConsumed");Assert.Equal(before+1,await db.Orders.CountAsync(Ct));
    }
    [Fact]
    public async Task Workflow_approval_is_owner_scoped_and_viewer_cannot_approve() {
        var fake=new FakeModel();using var app=App(fake);var a=await Session(UserRole.Admin);var b=await Session(UserRole.Admin);var viewer=await Session();
        using var owner=Authorized(app,a.Token);using var other=Authorized(app,b.Token);using var readOnly=Authorized(app,viewer.Token);
        var proposal=await SalesProposal(owner,fake);await Code(await Approve(other,proposal),HttpStatusCode.NotFound,"aiProposalNotFound");
        Assert.Equal(HttpStatusCode.Forbidden,(await Approve(readOnly,proposal)).StatusCode);
        await Code(await other.GetAsync($"/api/ai/agents/runs/{proposal.ExecutionId}/proposals",Ct),HttpStatusCode.NotFound,"aiExecutionNotFound");
        await Code(await Approve(owner,proposal,hash:new string('0',64)),HttpStatusCode.Conflict,"aiProposalChanged");
    }
    [Theory]
    [InlineData("price")] [InlineData("stock")] [InlineData("expiry")] [InlineData("incomplete")]
    public async Task Workflow_approval_rejects_changed_or_expired_facts_without_order_write(string change) {
        var fake=new FakeModel();using var app=App(fake);var user=await Session(UserRole.Admin);using var client=Authorized(app,user.Token);var proposal=await SalesProposal(client,fake);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);
        if(change=="price")await db.Products.Where(p=>p.Id==proposal.Facts.Lines[0].ProductId).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Price,21),Ct);
        if(change=="stock"){var balance=await db.WarehouseInventories.SingleAsync(i=>i.ProductId==proposal.Facts.Lines[0].ProductId&&i.WarehouseId==proposal.Facts.WarehouseId,Ct);balance.QuantityOnHand++;await db.SaveChangesAsync(Ct);}
        if(change=="expiry")await db.Set<AiOrderProposal>().Where(p=>p.Id==proposal.Id).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.ExpiresAt,DateTimeOffset.UtcNow.AddMinutes(-1)),Ct);
        if(change=="incomplete")await db.Set<AiExecution>().Where(e=>e.Id==proposal.ExecutionId).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.Outcome,"failure"),Ct);
        await Code(await Approve(client,proposal),HttpStatusCode.Conflict,change=="expiry"?"aiProposalExpired":change=="incomplete"?"aiWorkflowIncomplete":"aiProposalStale");
        Assert.Equal(before,await db.Orders.CountAsync(Ct));
    }
    [Fact]
    public async Task Workflow_cancel_prevents_approval_and_key_cannot_be_reused_on_another_proposal() {
        var fake=new FakeModel();using var app=App(fake);var user=await Session(UserRole.Admin);using var client=Authorized(app,user.Token);
        var first=await SalesProposal(client,fake);var second=await SalesProposal(client,fake);var key=Guid.NewGuid();Assert.Equal(HttpStatusCode.OK,(await Approve(client,first,key)).StatusCode);
        await Code(await Approve(client,second,key),HttpStatusCode.Conflict,"aiIdempotencyConflict");
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync($"/api/ai/agents/proposals/{second.Id}/cancel",null,Ct)).StatusCode);
        await Code(await Approve(client,second),HttpStatusCode.Conflict,"aiProposalConsumed");
    }
    [Fact]
    public async Task Workflow_concurrent_approvals_create_one_order() {
        var fake=new FakeModel();using var app=App(fake);using var secondApp=App(new FakeModel());var user=await Session(UserRole.Admin);using var a=Authorized(app,user.Token);using var b=Authorized(secondApp,user.Token);
        var proposal=await SalesProposal(a,fake);var key=Guid.NewGuid();var results=await Task.WhenAll(Approve(a,proposal,key),Approve(b,proposal,key));
        Assert.All(results,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode));
        var one=(await results[0].Content.ReadFromJsonAsync<ProposalView>(Ct))!;var two=(await results[1].Content.ReadFromJsonAsync<ProposalView>(Ct))!;Assert.Equal(one.ResultId,two.ResultId);
    }
    private sealed class FailProposalCommit : SaveChangesInterceptor {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default) {
            if(data.Context!.ChangeTracker.Entries<AiOrderProposal>().Any(e=>e.Entity.State=="approved"))
                foreach(var audit in data.Context.ChangeTracker.Entries<AuditLog>().Where(e=>e.State==EntityState.Added))audit.Entity.UserId=Guid.NewGuid();
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Workflow_order_and_approval_roll_back_together_on_real_database_failure() {
        var fake=new FakeModel();using var app=App(fake);var user=await Session(UserRole.Admin);using var client=Authorized(app,user.Token);var proposal=await SalesProposal(client,fake);
        using var scope=app.Services.CreateScope();var original=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await original.Orders.CountAsync(Ct);var audits=await original.AuditLogs.CountAsync(Ct);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new("sub",user.Id.ToString()),new("role","Admin"),new("sv","0")],"test","name","role"));
        var http=new Microsoft.AspNetCore.Http.HttpContextAccessor{HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=principal}};
        await using(var failing=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(original.Database.GetConnectionString()).AddInterceptors(new FailProposalCommit()).Options,http)) {
            var service=new WorkflowProposals(failing,scope.ServiceProvider.GetRequiredService<AiToolExecutor>(),new OrderService(failing),new ProcurementService(failing),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WorkflowProposals>>());
            await Assert.ThrowsAsync<DbUpdateException>(()=>service.Approve(proposal.Id,new(Guid.NewGuid(),proposal.Fingerprint),principal,Ct));
        }
        Assert.Equal(before,await original.Orders.CountAsync(Ct));Assert.Equal(audits,await original.AuditLogs.CountAsync(Ct));
        Assert.Equal("pending",await original.Set<AiOrderProposal>().Where(p=>p.Id==proposal.Id).Select(p=>p.State).SingleAsync(Ct));
        Assert.Equal("aiApprovalFailed",await original.Set<AiOrderProposal>().Where(p=>p.Id==proposal.Id).Select(p=>p.ErrorCategory).SingleAsync(Ct));
        Assert.Equal(HttpStatusCode.OK,(await Approve(client,proposal)).StatusCode);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Workflow_Turkish_procurement_uses_sequential_handoffs_and_only_purchase_API(bool history) {
        var user=await Session(UserRole.Admin);using var scope=fixture.App.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await SeedAgentData();
        var candidates=await scope.ServiceProvider.GetRequiredService<InventoryAnalysisAgent>().Analyze(new(DateTimeOffset.UtcNow.AddDays(-30),DateTimeOffset.UtcNow),Ct);
        var supplier=new Supplier{SupplierCode="WF"+Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),Name="Workflow supplier"};db.Add(supplier);
        if(history)db.PurchaseOrders.Add(new PurchaseOrder{SupplierId=supplier.Id,CreatedByUserId=user.Id,PurchaseOrderNumber="WF"+Guid.NewGuid().ToString("N"),Status=PurchaseOrderStatus.Submitted,
            Items=candidates.Products.Where(p=>p.SuggestedQuantity>0).Select(p=>new PurchaseOrderItem{ProductId=p.ProductId,QuantityOrdered=1,UnitCost=7,LineTotal=7}).ToList(),TotalAmount=7*candidates.Products.Count(p=>p.SuggestedQuantity>0)});
        await db.SaveChangesAsync(Ct);var before=await db.Orders.CountAsync(Ct);
        var fake=new FakeModel();fake.Turns.Enqueue(Final("tr"));using var app=App(fake);using var client=Authorized(app,user.Token);var run=await StartAgent(client);
        var response=await ExecuteAgent(client,run,"Son 30 günlük satışları analiz et, mevcut stokları kontrol et ve eksik ürünler için satın alma siparişi hazırla.","tr");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=(await response.Content.ReadFromJsonAsync<AgentResult>(Ct))!;Assert.Equal("procurement",result.Intent);Assert.NotNull(result.Sales);Assert.NotNull(result.Inventory);Assert.Null(result.OrderDraft);
        Assert.Equal(new[]{"orchestrator","sales","inventory","order","summary"},(await AgentStatus(client,run))!.Steps.Select(s=>s.Agent));
        Assert.Equal(1,fake.Requests);Assert.Equal(before,await db.Orders.CountAsync(Ct));
        if(history) {
            var proposal=Assert.Single(result.Proposals!,p=>p.Facts.CounterpartyId==supplier.Id);Assert.Equal("purchase",proposal.Kind);Assert.All(proposal.Facts.Lines,l=>Assert.Equal(7,l.UnitPrice));
            var approved=(await (await Approve(client,proposal)).Content.ReadFromJsonAsync<ProposalView>(Ct))!;Assert.Equal("Draft",approved.ResultStatus);
            Assert.True(await db.PurchaseOrders.AnyAsync(o=>o.Id==approved.ResultId&&o.SupplierId==supplier.Id,Ct));Assert.Equal(before,await db.Orders.CountAsync(Ct));
        }
    }
    [Fact]
    public async Task Workflow_interrupted_run_can_be_recovered_and_retried_without_replaying_writes() {
        var fake=new FakeModel();using var app=App(fake);var user=await Session();using var client=Authorized(app,user.Token);var id=await StartAgent(client);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<AiExecution>().Where(e=>e.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.Outcome,"running"),Ct);
        await db.Set<AiAgentRun>().Where(e=>e.ExecutionId==id).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.StartedAt,DateTimeOffset.UtcNow.AddMinutes(-10)),Ct);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync($"/api/ai/agents/runs/{id}/recover",null,Ct)).StatusCode);
        Assert.Equal("interrupted",(await AgentStatus(client,id))!.Execution.Outcome);
        var retried=await client.PostAsync($"/api/ai/agents/runs/{id}/retry",null,Ct);Assert.Equal(HttpStatusCode.Created,retried.StatusCode);
        using var json=System.Text.Json.JsonDocument.Parse(await retried.Content.ReadAsStringAsync(Ct));var next=json.RootElement.GetProperty("id").GetGuid();Assert.Equal(id,(await AgentStatus(client,next))!.RetryOf);
        Assert.Equal(0,fake.Requests);
    }
    [Fact]
    public async Task Workflow_persisted_cancel_interrupts_active_model_and_finalizes_history() {
        var fake=new FakeModel{Handler=async(_,_,token)=>{await Task.Delay(10000,token);return Final();}};using var app=App(fake);var user=await Session();using var client=Authorized(app,user.Token);var id=await StartAgent(client,"inventory");
        var running=ExecuteAgent(client,id);await Task.Delay(600,Ct);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync($"/api/ai/agents/runs/{id}/cancel",null,Ct)).StatusCode);
        await Code(await running,(HttpStatusCode)499,"aiCancelled");var status=(await AgentStatus(client,id))!;Assert.Equal("cancelled",status.Execution.Outcome);Assert.NotNull(status.CompletedAt);
    }
    [Fact]
    public async Task Workflow_missing_purchase_history_returns_only_missing_references_without_inventing_prices() {
        var user=await Session(UserRole.Admin);var fake=new FakeModel();using var app=App(fake);using var client=Authorized(app,user.Token);var seed=await SeedAgentData();var id=await StartAgent(client);
        using var scope=app.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<WorkflowProposals>();
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new("sub",user.Id.ToString()),new("role","Admin"),new("sv","0")],"test","name","role"));
        var window=new AgentWindow(DateTimeOffset.UtcNow.AddDays(-30),DateTimeOffset.UtcNow);
        var result=await service.Purchase(id,new(DateTimeOffset.UtcNow,window,1,false,[new(seed.Product,seed.ProductName,3,10,6,0.2m,11,8,"test")]),principal,Ct);
        Assert.Empty(result.Proposals);Assert.Equal(new[]{seed.ProductName},result.MissingReferences);
    }
    [Fact]
    public async Task Workflow_history_reloads_pending_approvals_and_verified_results_without_model_calls() {
        var fake=new FakeModel();using var app=App(fake);var user=await Session(UserRole.Admin);using var client=Authorized(app,user.Token);var proposal=await SalesProposal(client,fake);var calls=fake.Requests;
        var pending=(await client.GetFromJsonAsync<List<ProposalView>>($"/api/ai/agents/runs/{proposal.ExecutionId}/proposals",Ct))!;
        Assert.Equal(proposal.Fingerprint,Assert.Single(pending).Fingerprint);
        var saved=(await (await Approve(client,proposal)).Content.ReadFromJsonAsync<ProposalView>(Ct))!;Assert.True(saved.ResultVerified);
        var history=Assert.Single((await client.GetFromJsonAsync<List<ProposalView>>($"/api/ai/agents/runs/{proposal.ExecutionId}/proposals",Ct))!);
        Assert.Equal(saved.ResultId,history.ResultId);Assert.True(history.ResultVerified);Assert.NotNull(history.CompletedAt);Assert.Equal(calls,fake.Requests);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Orders.Where(o=>o.Id==history.ResultId).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.TotalAmount,1),Ct);
        var changed=Assert.Single((await client.GetFromJsonAsync<List<ProposalView>>($"/api/ai/agents/runs/{proposal.ExecutionId}/proposals",Ct))!);Assert.False(changed.ResultVerified);Assert.Equal("aiProposalResultMissing",changed.ErrorCategory);
    }
    [Fact]
    public async Task Workflow_active_execution_cannot_be_recovered_and_queued_cancellation_blocks_execution() {
        var fake=new FakeModel();using var app=App(fake);var user=await Session();using var client=Authorized(app,user.Token);var id=await StartAgent(client);
        await Code(await client.PostAsync($"/api/ai/agents/runs/{id}/recover",null,Ct),HttpStatusCode.Conflict,"aiWorkflowNotRecoverable");
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync($"/api/ai/agents/runs/{id}/cancel",null,Ct)).StatusCode);
        await Code(await ExecuteAgent(client,id),HttpStatusCode.Conflict,"aiAgentAlreadyStarted");Assert.Equal(0,fake.Requests);
    }
    [Fact]
    public async Task Workflow_retry_chain_is_bounded_and_remains_owner_isolated() {
        var fake=new FakeModel();using var app=App(fake);var user=await Session();var stranger=await Session();using var client=Authorized(app,user.Token);using var other=Authorized(app,stranger.Token);
        var id=await StartAgent(client);using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for(var retry=0;retry<3;retry++) {
            await db.Set<AiExecution>().Where(e=>e.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.Outcome,"timeout"),Ct);
            Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsync($"/api/ai/agents/runs/{id}/retry",null,Ct)).StatusCode);
            var response=await client.PostAsync($"/api/ai/agents/runs/{id}/retry",null,Ct);
            if(retry==2){await Code(response,HttpStatusCode.Conflict,"aiAgentLimit");break;}
            Assert.Equal(HttpStatusCode.Created,response.StatusCode);using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));id=json.RootElement.GetProperty("id").GetGuid();
        }
        Assert.Equal(0,fake.Requests);
    }
    [Theory]
    [InlineData("cost")] [InlineData("stock")] [InlineData("longName")]
    public async Task Workflow_purchase_revalidates_latest_cost_evidence_and_inventory(string change) {
        var fake=new FakeModel();using var app=App(fake);var user=await Session(UserRole.Admin);using var client=Authorized(app,user.Token);var seed=await SeedAgentData();var run=await StartAgent(client);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if(change=="longName")await db.Products.Where(p=>p.Id==seed.Product).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Name,new string('A',140)),Ct);
        var supplier=new Supplier{SupplierCode="WF"+Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),Name="Price evidence supplier"};db.Add(supplier);
        PurchaseOrder History(decimal cost)=>new(){SupplierId=supplier.Id,CreatedByUserId=user.Id,PurchaseOrderNumber="WF"+Guid.NewGuid().ToString("N"),Status=PurchaseOrderStatus.Submitted,OrderDate=DateTimeOffset.UtcNow.AddSeconds((double)cost),TotalAmount=cost,Items=[new(){ProductId=seed.Product,QuantityOrdered=1,UnitCost=cost,LineTotal=cost}]};
        db.Add(History(7));await db.SaveChangesAsync(Ct);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new("sub",user.Id.ToString()),new("role","Admin"),new("sv","0")],"test","name","role"));
        var result=await scope.ServiceProvider.GetRequiredService<WorkflowProposals>().Purchase(run,new(DateTimeOffset.UtcNow,new(DateTimeOffset.UtcNow.AddDays(-30),DateTimeOffset.UtcNow),1,false,[new(seed.Product,seed.ProductName,3,10,6,0.2m,11,8,"test")]),principal,Ct);
        var proposal=Assert.Single(result.Proposals);
        await db.Set<AiExecution>().Where(e=>e.Id==run).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.Outcome,"success"),Ct);
        if(change=="longName"){Assert.Equal(140,proposal.Facts.Lines[0].Product.Length);Assert.Equal(HttpStatusCode.OK,(await Approve(client,proposal)).StatusCode);return;}
        if(change=="cost")db.Add(History(8));else{var balance=await db.WarehouseInventories.SingleAsync(i=>i.ProductId==seed.Product,Ct);balance.QuantityOnHand++;}
        await db.SaveChangesAsync(Ct);var before=await db.PurchaseOrders.CountAsync(Ct);
        await Code(await Approve(client,proposal),HttpStatusCode.Conflict,"aiProposalStale");Assert.Equal(before,await db.PurchaseOrders.CountAsync(Ct));
    }
    [Fact]
    public async Task Workflow_revoked_identity_cannot_approve_an_existing_owned_proposal() {
        var fake=new FakeModel();using var app=App(fake);var user=await Session(UserRole.Admin);using var client=Authorized(app,user.Token);var proposal=await SalesProposal(client,fake);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);
        await db.Users.Where(u=>u.Id==user.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.IsActive,false).SetProperty(u=>u.SecurityVersion,u=>u.SecurityVersion+1),Ct);
        Assert.Equal(HttpStatusCode.Unauthorized,(await Approve(client,proposal)).StatusCode);Assert.Equal(before,await db.Orders.CountAsync(Ct));
    }
}
