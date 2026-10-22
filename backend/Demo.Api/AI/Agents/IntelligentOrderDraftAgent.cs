using System.Security.Claims;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
namespace Demo.Api.AI.Agents;

public sealed record DraftProduct(Guid Id,string Name,decimal Price,int Stock);
public sealed record DraftWarehouse(Guid Id,string Name);
public interface IOrderDraftReader {
    Task<List<DraftCandidate>> Customers(string name,ClaimsPrincipal user,CancellationToken ct);
    Task<List<DraftProduct>> Products(string name,Guid warehouse,ClaimsPrincipal user,CancellationToken ct);
    Task<DraftWarehouse> Warehouse(ClaimsPrincipal user,CancellationToken ct);
}
// Only typed reads are exposed to the draft agent; no SaveChanges, order creation or stock mutation contract.
public sealed class OrderDraftReader(AppDbContext db,AiToolExecutor access):IOrderDraftReader
{
    public async Task<List<DraftCandidate>> Customers(string name,ClaimsPrincipal user,CancellationToken ct) {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);var key=name.ToLowerInvariant();var q=db.Customers.AsNoTracking().Where(c=>c.IsActive);
        var exact=await q.Where(c=>c.Name.ToLower()==key).Select(c=>new DraftCandidate(c.Id,c.Name)).Take(5).ToListAsync(ct);
        return exact.Count>0?exact:await q.Where(c=>c.Name.ToLower().Contains(key)).OrderBy(c=>c.Name).Select(c=>new DraftCandidate(c.Id,c.Name)).Take(5).ToListAsync(ct);
    }
    public async Task<DraftWarehouse> Warehouse(ClaimsPrincipal user,CancellationToken ct) {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);
        return await db.Warehouses.AsNoTracking().Where(w=>w.IsDefault&&w.IsActive).Select(w=>new DraftWarehouse(w.Id,w.Name)).SingleOrDefaultAsync(ct)
            ??throw new BusinessException("aiToolFailure",503);
    }
    public async Task<List<DraftProduct>> Products(string name,Guid warehouse,ClaimsPrincipal user,CancellationToken ct) {
        await access.Recheck(user,AccessPolicies.BusinessRead,ct);var key=name.ToLowerInvariant();var q=db.Products.AsNoTracking().Where(p=>p.IsActive);
        var exact=await q.Where(p=>p.Name.ToLower()==key).Select(p=>p.Id).Take(5).ToArrayAsync(ct);
        var found=exact.Length>0?q.Where(p=>exact.Contains(p.Id)):q.Where(p=>p.Name.ToLower().Contains(key));
        return await found.OrderBy(p=>p.Name).Take(5).Select(p=>new DraftProduct(p.Id,p.Name,p.Price,
            db.WarehouseInventories.Where(i=>i.ProductId==p.Id&&i.WarehouseId==warehouse).Select(i=>(int?)i.QuantityOnHand).SingleOrDefault()??0)).ToListAsync(ct);
    }
}
public sealed class IntelligentOrderDraftAgent(IOrderDraftReader reader)
{
    public async Task<ResolvedOrderDraft> Prepare(AgentPlan plan,ClaimsPrincipal user,CancellationToken ct)
    {
        if(plan.Kind!="order"||plan.Lines.Count>10)throw new BusinessException("aiInvalidResponse",502);
        var issues=new List<DraftClarification>();Guid? customerId=null;string? customerName=null;
        if(string.IsNullOrWhiteSpace(plan.Customer))issues.Add(new("customer","","missing",[]));
        else {
            var found=await reader.Customers(plan.Customer,user,ct);
            if(found.Count!=1)issues.Add(new("customer",plan.Customer,found.Count==0?"notFound":"ambiguous",found));
            else {customerId=found[0].Id;customerName=found[0].Name;}
        }
        var warehouse=await reader.Warehouse(user,ct);var lines=new List<ResolvedDraftLine>();var seen=new HashSet<Guid>();
        if(plan.Lines.Count==0)issues.Add(new("product","","missing",[]));
        foreach(var item in plan.Lines) {
            if(string.IsNullOrWhiteSpace(item.Product)||item.Product.Length>120||item.Quantity is <1 or >10000)throw new BusinessException("aiInvalidResponse",502);
            if(item.Quantity is null) {issues.Add(new("quantity",item.Product,"missing",[]));continue;}
            var found=await reader.Products(item.Product,warehouse.Id,user,ct);
            if(found.Count!=1) {issues.Add(new("product",item.Product,found.Count==0?"notFound":"ambiguous",found.Select(p=>new DraftCandidate(p.Id,p.Name)).ToList()));continue;}
            var product=found[0];if(product.Price<0||product.Stock<0)throw new BusinessException("aiToolFailure",503);
            if(!seen.Add(product.Id)){issues.Add(new("product",item.Product,"duplicate",[]));continue;}
            var total=checked(product.Price*item.Quantity.Value);
            lines.Add(new(product.Id,product.Name,item.Quantity.Value,product.Price,total,product.Stock,Math.Max(0,item.Quantity.Value-product.Stock)));
        }
        decimal? sum=issues.Count==0?lines.Sum(l=>l.LineTotal):null;
        if(sum>9999999999999999.99m)throw new BusinessException("aiInvalidRequest");
        return new(customerId,customerName,warehouse.Id,warehouse.Name,lines,sum,"USD",issues);
    }
}
