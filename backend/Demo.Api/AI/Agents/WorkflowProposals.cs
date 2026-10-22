using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.AI.Agents;

public sealed class AiOrderProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExecutionId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "sales";
    public string State { get; set; } = "pending";
    public string Payload { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(15);
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? IdempotencyKey { get; set; }
    public Guid? ResultId { get; set; }
    public string? ResultNumber { get; set; }
    public string? ResultStatus { get; set; }
    public string? ErrorCategory { get; set; }
}
public sealed record ProposalLine(Guid ProductId, string Product, int Quantity, decimal UnitPrice, long Stock, Guid? PriceSourceId = null);
public sealed record ProposalFacts(Guid CounterpartyId, string Counterparty, Guid? WarehouseId, string? Warehouse, List<ProposalLine> Lines, string Currency = "USD");
public sealed record ProposalView(Guid Id, Guid ExecutionId, string Kind, string State, string Fingerprint, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, ProposalFacts Facts, Guid? ResultId, string? ResultNumber, string? ResultStatus, string? ErrorCategory, DateTimeOffset? CompletedAt = null, bool ResultVerified = false);
public sealed record ProposalApproval(Guid IdempotencyKey, string Fingerprint);
public sealed record PurchasePreparation(List<ProposalView> Proposals, List<string> MissingReferences);

// Only backend-resolved facts enter this store. No client or model payload is accepted as a proposal.
public sealed class WorkflowProposals(AppDbContext db, AiToolExecutor access, OrderService orders, ProcurementService purchases,
    IServiceScopeFactory scopes, ILogger<WorkflowProposals> logger, Quality.QualityTelemetry? telemetry=null)
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<AiOrderProposal>(e => {
            e.ToTable("AiOrderProposals", t => {
                t.HasCheckConstraint("CK_AiOrderProposals_Kind", "\"Kind\" IN ('sales','purchase')");
                t.HasCheckConstraint("CK_AiOrderProposals_State", "\"State\" IN ('pending','approved','cancelled','expired','stale')");
            });
            e.HasKey(p => p.Id); e.Property(p => p.Kind).HasMaxLength(16); e.Property(p => p.State).HasMaxLength(16);
            e.Property(p => p.Payload).HasMaxLength(20000); e.Property(p => p.Fingerprint).HasMaxLength(64);
            e.Property(p => p.ResultNumber).HasMaxLength(50); e.Property(p => p.ResultStatus).HasMaxLength(24); e.Property(p => p.ErrorCategory).HasMaxLength(64);
            e.HasIndex(p => new { p.UserId, p.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasIndex(p => new { p.ExecutionId, p.CreatedAt });
            e.HasOne<AiExecution>().WithMany().HasForeignKey(p => p.ExecutionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
    private static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
    private static ProposalFacts Facts(AiOrderProposal row) => JsonSerializer.Deserialize<ProposalFacts>(row.Payload, AiAssistantService.Json)!;
    private static ProposalView View(AiOrderProposal p) => new(p.Id,p.ExecutionId,p.Kind,p.State=="pending"&&p.ExpiresAt<=DateTimeOffset.UtcNow?"expired":p.State,p.Fingerprint,p.CreatedAt,p.ExpiresAt,Facts(p),p.ResultId,p.ResultNumber,p.ResultStatus,p.ErrorCategory,p.CompletedAt,p.State=="approved"&&p.ErrorCategory is null);
    private async Task<AiOrderProposal> Own(Guid id, ClaimsPrincipal user, bool locked, CancellationToken ct)
    {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct); var owner=Owner(user);
        var row = locked ? await db.Set<AiOrderProposal>().FromSqlInterpolated($"SELECT * FROM \"AiOrderProposals\" WHERE \"Id\"={id} AND \"UserId\"={owner} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new BusinessException("aiProposalNotFound",404)
            : await db.Set<AiOrderProposal>().AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id&&p.UserId==owner,ct) ?? throw new BusinessException("aiProposalNotFound",404);
        if(!await db.Set<AiExecution>().AnyAsync(e=>e.Id==row.ExecutionId&&e.UserId==owner&&e.ExpiresAt>DateTimeOffset.UtcNow,ct))throw new BusinessException("aiProposalNotFound",404);
        return row;
    }
    private async Task<ProposalView> Add(Guid execution,string kind,ProposalFacts facts,ClaimsPrincipal user,CancellationToken ct)
    {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);
        var owner=Owner(user);
        if(!await db.Set<AiExecution>().AnyAsync(e=>e.Id==execution&&e.UserId==owner&&e.ExpiresAt>DateTimeOffset.UtcNow,ct))throw new BusinessException("aiExecutionNotFound",404);
        if(facts.Lines.Count is <1 or >10||facts.Lines.Any(l=>l.Quantity is <1 or >100000||l.UnitPrice<0)||facts.Lines.Sum(l=>l.UnitPrice*l.Quantity)>9999999999999999.99m)throw new BusinessException("aiInvalidRequest");
        var payload=JsonSerializer.Serialize(facts,AiAssistantService.Json);
        var row=new AiOrderProposal{ExecutionId=execution,UserId=owner,Kind=kind,Payload=payload};
        // Binds identity, action scope and exact immutable facts, not just a client-rendered amount.
        row.Fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{row.Id:N}|{execution:N}|{owner:N}|{kind}|{payload}")));
        db.Add(row);await db.SaveChangesAsync(ct);return View(row);
    }
    public async Task<ProposalView?> Sales(Guid execution,ResolvedOrderDraft draft,ClaimsPrincipal user,CancellationToken ct)
    {
        if(draft.Clarifications.Count>0||draft.CustomerId is null||draft.WarehouseId is null||draft.Total is null)return null;
        return await Add(execution,"sales",new(draft.CustomerId.Value,draft.Customer!,draft.WarehouseId,draft.Warehouse,
            draft.Lines.Select(l=>new ProposalLine(l.ProductId,l.Product,l.Quantity,l.UnitPrice,l.AvailableStock)).ToList()),user,ct);
    }
    public async Task<PurchasePreparation> Purchase(Guid execution,InventoryAnalysis inventory,ClaimsPrincipal user,CancellationToken ct)
    {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);
        var groups=new Dictionary<Guid,(string Name,List<ProposalLine> Lines)>();var missing=new List<string>();
        foreach(var row in inventory.Products.Where(p=>p.SuggestedQuantity>0)) {
            // Last non-cancelled submitted purchase is evidence, not an invented supplier quote or sales price.
            var source=await db.PurchaseOrderItems.AsNoTracking().Where(i=>i.ProductId==row.ProductId&&i.PurchaseOrder.Supplier.IsActive
                &&i.PurchaseOrder.Status!=PurchaseOrderStatus.Draft&&i.PurchaseOrder.Status!=PurchaseOrderStatus.Cancelled)
                .OrderByDescending(i=>i.PurchaseOrder.OrderDate).ThenByDescending(i=>i.Id)
                .Select(i=>new {i.Id,i.UnitCost,i.PurchaseOrder.SupplierId,i.PurchaseOrder.Supplier.Name,ProductName=i.Product.Name}).FirstOrDefaultAsync(ct);
            if(source is null||row.SuggestedQuantity>100000){missing.Add(row.Product);continue;}
            if(!groups.TryGetValue(source.SupplierId,out var group)){group=(source.Name,[]);groups[source.SupplierId]=group;}
            group.Lines.Add(new(row.ProductId,source.ProductName,row.SuggestedQuantity!.Value,source.UnitCost,row.CurrentStock,source.Id));
        }
        var result=new List<ProposalView>();
        foreach(var (supplier,group) in groups)result.Add(await Add(execution,"purchase",new(supplier,group.Name,null,null,group.Lines),user,ct));
        return new(result,missing);
    }
    public async Task<List<ProposalView>> List(Guid execution,ClaimsPrincipal user,CancellationToken ct)
    {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);var owner=Owner(user);
        if(!await db.Set<AiExecution>().AnyAsync(e=>e.Id==execution&&e.UserId==owner&&e.ExpiresAt>DateTimeOffset.UtcNow,ct))throw new BusinessException("aiExecutionNotFound",404);
        var rows=await db.Set<AiOrderProposal>().AsNoTracking().Where(p=>p.ExecutionId==execution&&p.UserId==owner).OrderBy(p=>p.CreatedAt).Take(10).ToListAsync(ct);
        foreach(var row in rows.Where(p=>p.State=="approved"))try{await VerifyResult(row,ct);}catch(BusinessException ex){row.ErrorCategory=ex.Code;}
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);return rows.Select(View).ToList();
    }
    public async Task<ProposalView> Cancel(Guid id,ClaimsPrincipal user,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);var row=await Own(id,user,true,ct);
        if(row.State!="pending")throw new BusinessException("aiProposalConsumed",409);
        row.State="cancelled";row.CompletedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return View(row);
    }
    public async Task<ProposalView> Approve(Guid id,ProposalApproval input,ClaimsPrincipal user,CancellationToken ct)
    {
        using var trace=telemetry?.Begin("approval","human-approval");
        try { return await ApproveCore(id,input,user,ct); }
        catch(Exception error) {
            trace?.Fail(error);
            var category=error is BusinessException business?business.Code:error is OperationCanceledException?"aiCancelled":"aiApprovalFailed";
            logger.LogWarning("AI approval proposal {ProposalId} errorCategory {Category}",id,category);
            // Core's transaction has rolled back/disposed before recording this bounded metadata-only failure.
            if(Guid.TryParse(user.FindFirstValue("sub"),out var owner))try {
                await using var scope=scopes.CreateAsyncScope();var metadata=scope.ServiceProvider.GetRequiredService<AppDbContext>();
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await metadata.Set<AiOrderProposal>().Where(p=>p.Id==id&&p.UserId==owner&&p.State=="pending")
                    .ExecuteUpdateAsync(s=>s.SetProperty(p=>p.ErrorCategory,category),deadline.Token);
            } catch(Exception) { logger.LogWarning("AI approval failure metadata unavailable for {ProposalId}",id); }
            throw;
        }
    }
    private async Task<ProposalView> ApproveCore(Guid id,ProposalApproval input,ClaimsPrincipal user,CancellationToken ct)
    {
        if(input.IdempotencyKey==Guid.Empty||input.Fingerprint?.Length!=64)throw new BusinessException("aiInvalidRequest");
        await access.Recheck(user,AccessPolicies.OrderManage,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var actor=Owner(user);
        await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\"={actor} FOR SHARE").AsNoTracking().SingleAsync(ct);
        await access.Recheck(user,AccessPolicies.OrderManage,ct);
        // Serializes key reuse across proposals as well as same-proposal concurrent approvals.
        var key=input.IdempotencyKey.ToString("N");await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",ct);
        var row=await Own(id,user,true,ct);
        if(row.Fingerprint!=input.Fingerprint)throw new BusinessException("aiProposalChanged",409);
        if(row.State=="approved") {
            if(row.IdempotencyKey!=input.IdempotencyKey)throw new BusinessException("aiProposalConsumed",409);
            await VerifyResult(row,ct);await tx.CommitAsync(ct);return View(row);
        }
        if(row.State!="pending")throw new BusinessException("aiProposalConsumed",409);
        if(!await db.Set<AiExecution>().AnyAsync(e=>e.Id==row.ExecutionId&&e.UserId==row.UserId&&e.Outcome=="success"&&e.ExpiresAt>DateTimeOffset.UtcNow,ct))throw new BusinessException("aiWorkflowIncomplete",409);
        if(row.ExpiresAt<=DateTimeOffset.UtcNow){row.State="expired";row.ErrorCategory="aiProposalExpired";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw new BusinessException("aiProposalExpired",409);}
        if(await db.Set<AiOrderProposal>().AnyAsync(p=>p.UserId==row.UserId&&p.IdempotencyKey==input.IdempotencyKey,ct))throw new BusinessException("aiIdempotencyConflict",409);
        var facts=Facts(row);
        var warehouses=facts.WarehouseId is Guid w?new[]{w}:await db.Warehouses.Select(w=>w.Id).ToArrayAsync(ct);
        await InventoryService.LockWarehouses(db,warehouses,ct,requireActive:row.Kind=="sales");
        await InventoryService.LockProducts(db,facts.Lines.Select(l=>l.ProductId),ct,requireActive:true);
        var stale=false;
        if(row.Kind=="sales") {
            var customer=await db.Customers.FromSqlInterpolated($"SELECT * FROM \"Customers\" WHERE \"Id\"={facts.CounterpartyId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if(customer is null||!customer.IsActive)throw new BusinessException("customerUnavailable");
            if(customer.Name!=facts.Counterparty)stale=true;
        } else {
            var supplier=await db.Suppliers.FromSqlInterpolated($"SELECT * FROM \"Suppliers\" WHERE \"Id\"={facts.CounterpartyId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if(supplier is null||!supplier.IsActive)throw new BusinessException("supplierUnavailable");
            if(supplier.Name!=facts.Counterparty)stale=true;
        }
        if(facts.WarehouseId is Guid warehouseId&&await db.Warehouses.Where(w=>w.Id==warehouseId).Select(w=>w.Name).SingleAsync(ct)!=facts.Warehouse)stale=true;
        foreach(var line in facts.Lines) {
            var stock=await db.WarehouseInventories.Where(i=>i.ProductId==line.ProductId&&(facts.WarehouseId==null||i.WarehouseId==facts.WarehouseId)).SumAsync(i=>(long)i.QuantityOnHand,ct);
            var price=row.Kind=="sales"?await db.Products.Where(p=>p.Id==line.ProductId).Select(p=>p.Price).SingleAsync(ct)
                :await db.PurchaseOrderItems.Where(i=>i.Id==line.PriceSourceId&&i.PurchaseOrder.SupplierId==facts.CounterpartyId&&i.PurchaseOrder.Status!=PurchaseOrderStatus.Cancelled).Select(i=>(decimal?)i.UnitCost).SingleOrDefaultAsync(ct);
            if(stock!=line.Stock||price!=line.UnitPrice)stale=true;
            if(await db.Products.Where(p=>p.Id==line.ProductId).Select(p=>p.Name).SingleAsync(ct)!=line.Product)stale=true;
            if(row.Kind=="purchase") {
                var latest=await db.PurchaseOrderItems.Where(i=>i.ProductId==line.ProductId&&i.PurchaseOrder.Supplier.IsActive&&i.PurchaseOrder.Status!=PurchaseOrderStatus.Draft&&i.PurchaseOrder.Status!=PurchaseOrderStatus.Cancelled)
                    .OrderByDescending(i=>i.PurchaseOrder.OrderDate).ThenByDescending(i=>i.Id).Select(i=>(Guid?)i.Id).FirstOrDefaultAsync(ct);
                if(latest!=line.PriceSourceId)stale=true;
            }
        }
        if(stale){row.State="stale";row.ErrorCategory="aiProposalStale";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw new BusinessException("aiProposalStale",409);}
        await access.Recheck(user,AccessPolicies.OrderManage,ct);
        using var orderTrace=telemetry?.Begin("order",row.Kind,row.ExecutionId);
        if(row.Kind=="sales") {
            var request=new OrderRequest(facts.CounterpartyId,facts.Lines.Select(l=>new OrderItemRequest(l.ProductId,l.Quantity)).ToList(),facts.WarehouseId);
            var result=await orders.Create(request,ct);row.ResultId=result.Id;row.ResultNumber=result.OrderNumber;row.ResultStatus=result.Status.ToString();
        } else {
            var request=new PurchaseRequest(facts.CounterpartyId,null,"",facts.Lines.Select(l=>new PurchaseItemRequest(l.ProductId,l.Quantity,l.UnitPrice)).ToList());
            var result=(await purchases.Save(null,request,Owner(user),ct))!;row.ResultId=result.Id;row.ResultNumber=result.PurchaseOrderNumber;row.ResultStatus=result.Status.ToString();
        }
        row.State="approved";row.IdempotencyKey=input.IdempotencyKey;row.CompletedAt=DateTimeOffset.UtcNow;row.ErrorCategory=null;
        await VerifyResult(row,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return View(row);
    }
    private async Task VerifyResult(AiOrderProposal row,CancellationToken ct)
    {
        var facts=Facts(row);var total=facts.Lines.Sum(l=>l.UnitPrice*l.Quantity);
        if(row.Kind=="sales") {
            var order=row.ResultId is Guid id?await orders.Get(id,ct):null;
            if(order is null||order.CustomerId!=facts.CounterpartyId||order.WarehouseId!=facts.WarehouseId||order.TotalAmount!=total||order.Items.Count!=facts.Lines.Count
                ||facts.Lines.Any(l=>!order.Items.Any(i=>i.ProductId==l.ProductId&&i.Quantity==l.Quantity&&i.UnitPrice==l.UnitPrice)))throw new BusinessException("aiProposalResultMissing",409);
            row.ResultStatus=order.Status.ToString();
        } else {
            var order=row.ResultId is Guid id?await purchases.Get(id,ct):null;
            if(order is null||order.SupplierId!=facts.CounterpartyId||order.TotalAmount!=total||order.Items.Count!=facts.Lines.Count
                ||facts.Lines.Any(l=>!order.Items.Any(i=>i.ProductId==l.ProductId&&i.QuantityOrdered==l.Quantity&&i.UnitCost==l.UnitPrice)))throw new BusinessException("aiProposalResultMissing",409);
            row.ResultStatus=order.Status.ToString();
        }
    }
}
