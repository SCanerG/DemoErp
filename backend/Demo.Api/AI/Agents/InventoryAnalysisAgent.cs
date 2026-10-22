using Demo.Api.Contracts;
using Demo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Demo.Api.Services;
namespace Demo.Api.AI.Agents;

public sealed class InventoryAnalysisAgent(ReportingService reports,AppDbContext db)
{
    public static ReplenishmentRow Calculate(InventoryReportRow row,long sold,AgentWindow window)
    {
        if(row.QuantityOnHand<0||row.MinimumStockLevel<0||sold<0||window.Start>=window.End)throw new Demo.Api.Errors.BusinessException("aiToolFailure",503);
        var daily=decimal.Round(sold/(decimal)(window.End-window.Start).TotalDays,4);
        // Explicit policy, not a forecast: clear the low-stock threshold and cover 14 days of constant historical demand.
        var target=Math.Max((decimal)row.MinimumStockLevel+1,decimal.Ceiling(daily*14));
        var sufficient=row.MinimumStockLevel>0||sold>0;
        int? trustedTarget=sufficient&&target<=int.MaxValue?(int)target:null;
        return new(row.Id,row.Product.Length>120?row.Product[..120]:row.Product,row.QuantityOnHand,row.MinimumStockLevel,sold,daily,
            trustedTarget,trustedTarget is int t?Math.Max(0,t-row.QuantityOnHand):null,sufficient?"minimum_plus_one_or_14_day_constant_demand":"insufficient_threshold_and_sales");
    }
    public async Task<InventoryAnalysis> Analyze(AgentWindow window,CancellationToken ct,Guid? productId=null,int? workflowThreshold=null)
    {
        var query=db.Inventories.AsNoTracking().Where(i=>i.Product.IsActive&&(productId==null||i.ProductId==productId)
            &&(workflowThreshold==null?i.QuantityOnHand<=i.MinimumStockLevel:i.QuantityOnHand<workflowThreshold));
        var total=await query.CountAsync(ct);
        var rows=await query.OrderBy(i=>i.QuantityOnHand).ThenBy(i=>i.ProductId).Take(10)
            .Select(i=>new InventoryReportRow(i.ProductId,i.Product.Name,i.Product.Category.Name,i.QuantityOnHand,i.MinimumStockLevel,
                i.QuantityOnHand==0?"Out":"Low",i.UpdatedAt)).ToListAsync(ct);
        var ids=rows.Select(r=>r.Id).ToArray();
        var quantities=await reports.CompletedQuantities(window.Start,window.End,ids,ct);
        return new(DateTimeOffset.UtcNow,window,total,total>rows.Count,rows.Select(r=>workflowThreshold is int threshold
            ?Calculate(r with{MinimumStockLevel=threshold},quantities.GetValueOrDefault(r.Id),window)with{MinimumStock=r.MinimumStockLevel,Basis="workflow_threshold_plus_one_or_14_day_constant_demand"}
            :Calculate(r,quantities.GetValueOrDefault(r.Id),window)).ToList());
    }
}
