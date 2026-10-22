using Demo.Api.AI.Agents;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI.Monitoring;

public interface IMonitoringAnalysis
{
    Task<MonitoringAnalysis> Analyze(string kind, MonitoringSettings settings, MonitoringScope scope, DateTimeOffset now, CancellationToken ct);
}

// This capability exposes fixed read-only reports, not arbitrary tools, principals, SQL or business writes.
public sealed class MonitoringAnalysisService(AppDbContext db, ReportingService reports) : IMonitoringAnalysis
{
    public static bool Significant(decimal current, decimal previous, int currentSamples, int previousSamples, MonitoringSettings settings)
        => currentSamples >= settings.MinimumSamples && previousSamples >= settings.MinimumSamples && previous > 0
        && Math.Abs(current - previous) >= settings.AbsoluteChange && Math.Abs((current - previous) / previous * 100) >= settings.PercentageChange;

    public async Task<MonitoringAnalysis> Analyze(string kind, MonitoringSettings settings, MonitoringScope scope, DateTimeOffset now, CancellationToken ct)
    {
        scope.Demand(); settings.Validate(); var findings = new List<MonitoringFinding>(); var diagnostics = new List<string>(); var complete = true;
        var start = now.AddDays(-settings.PeriodDays); var before = start.AddDays(-settings.PeriodDays);
        string Name(string value) => value.Length > 160 ? value[..160] : value;
        void Add(MonitoringFinding finding) { if (findings.Count < 250) findings.Add(finding); else complete = false; }
        switch (kind)
        {
            case "inventory":
                var rows = await reports.Inventory(new() { Sort = "quantityOnHand", Descending = false }).Take(201).ToListAsync(ct);
                if (rows.Count > 200) { rows = rows.Take(200).ToList(); complete = false; }
                foreach (var batch in rows.Chunk(10))
                {
                    var sold = await reports.CompletedQuantities(start, now, batch.Select(r => r.Id).ToArray(), ct);
                    foreach (var row in batch)
                    {
                        if (row.QuantityOnHand > row.MinimumStockLevel) continue;
                        var fact = InventoryAnalysisAgent.Calculate(row, sold.GetValueOrDefault(row.Id), new(start, now));
                        var metrics = new Dictionary<string, decimal> { ["stock"] = row.QuantityOnHand, ["minimum"] = row.MinimumStockLevel, ["sold"] = fact.CompletedQuantity, ["dailyAverage"] = fact.DailyAverage };
                        if (fact.SuggestedQuantity is int quantity) metrics["suggestedQuantity"] = quantity;
                        else diagnostics.Add("inventoryThresholdAndDemandMissing");
                        Add(new("inventory:" + row.Id, row.QuantityOnHand == 0 ? "outOfStock" : "criticalInventory", row.QuantityOnHand == 0 ? "critical" : "warning", "product", row.Id, Name(row.Product), metrics));
                    }
                }
                var consumption = await db.InventoryMovements.AsNoTracking().Where(m => m.CreatedAt >= before && m.CreatedAt < now
                    && (m.MovementType == MovementType.StockOut || m.MovementType == MovementType.ProductionConsumption || m.MovementType == MovementType.OrderDeduction))
                    .GroupBy(m => new { m.ProductId, m.Product.Name }).Select(g => new { g.Key.ProductId, g.Key.Name,
                        Current = g.Where(m => m.CreatedAt >= start).Sum(m => (long)m.Quantity), Previous = g.Where(m => m.CreatedAt < start).Sum(m => (long)m.Quantity),
                        CurrentSamples = g.Count(m => m.CreatedAt >= start), PreviousSamples = g.Count(m => m.CreatedAt < start) }).OrderBy(x => x.ProductId).Take(201).ToListAsync(ct);
                if (consumption.Count > 200) complete = false;
                foreach (var row in consumption.Take(200)) if (row.Current > row.Previous && Significant(row.Current, row.Previous, row.CurrentSamples, row.PreviousSamples, settings))
                    Add(new("consumption:" + row.ProductId, "consumptionChange", "warning", "product", row.ProductId, Name(row.Name), new() { ["currentQuantity"] = row.Current, ["previousQuantity"] = row.Previous, ["currentSamples"] = row.CurrentSamples, ["previousSamples"] = row.PreviousSamples }));
                diagnostics.Add("consumptionExcludesTransfersAdjustmentsAndReturns");
                break;
            case "orders":
                var purchases = await db.PurchaseOrders.AsNoTracking().Where(o => (o.Status == PurchaseOrderStatus.Submitted || o.Status == PurchaseOrderStatus.PartiallyReceived)
                    && o.ExpectedDeliveryDate != null && o.ExpectedDeliveryDate <= now.AddDays(settings.ApproachingDays))
                    .OrderBy(o => o.ExpectedDeliveryDate).Take(201).Select(o => new { o.Id, o.PurchaseOrderNumber, o.ExpectedDeliveryDate }).ToListAsync(ct);
                if (purchases.Count > 200) complete = false;
                foreach (var row in purchases.Take(200)) Add(new("purchase:" + row.Id, row.ExpectedDeliveryDate < now ? "overduePurchase" : "approachingPurchase",
                    row.ExpectedDeliveryDate < now ? "critical" : "warning", "purchase", row.Id, Name(row.PurchaseOrderNumber), new(), row.ExpectedDeliveryDate));
                var stalled = await db.Orders.AsNoTracking().Where(o => (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed) && (o.UpdatedAt ?? o.CreatedAt) < now.AddDays(-settings.StalledDays))
                    .OrderBy(o => o.CreatedAt).Take(201).Select(o => new { o.Id, o.OrderNumber }).ToListAsync(ct);
                if (stalled.Count > 200) complete = false;
                foreach (var row in stalled.Take(200)) Add(new("stalled:" + row.Id, "stalledSalesOrder", "warning", "order", row.Id, Name(row.OrderNumber), new() { ["stalledDaysThreshold"] = settings.StalledDays }));
                var approvals = await (from p in db.Set<AiOrderProposal>().AsNoTracking() join e in db.Set<AiExecution>() on p.ExecutionId equals e.Id
                    join u in db.Users on p.UserId equals u.Id where p.State == "pending" && p.ExpiresAt > now && e.ExpiresAt > now && e.Outcome == "success" && u.IsActive
                    select new { p.Id, p.UserId }).OrderBy(p => p.Id).Take(201).ToListAsync(ct);
                if (approvals.Count > 200) complete = false;
                foreach (var row in approvals.Take(200)) Add(new("approval:" + row.Id, "pendingApproval", "warning", "approval", row.Id, "", new(), OwnerId: row.UserId));
                diagnostics.Add("salesOrdersHaveNoDeliveryDate");
                diagnostics.Add("purchaseOrdersWithoutDueDatesAreNotOverdue");
                break;
            case "sales":
                var current = await reports.SalesMetrics(start, now, ct); var previous = await reports.SalesMetrics(before, start, ct);
                if (current.CompletedOrders < settings.MinimumSamples || previous.CompletedOrders < settings.MinimumSamples) diagnostics.Add("insufficientSalesHistory");
                if (Significant(current.Sales, previous.Sales, current.CompletedOrders, previous.CompletedOrders, settings))
                    Add(new("sales:workspace", "salesChange", "warning", "sales", null, "", Metrics(current.Sales, previous.Sales, current.CompletedOrders, previous.CompletedOrders)));
                var productNow = await reports.Products(new() { Start = start, End = now }).Take(101).ToListAsync(ct);
                var productBefore = await reports.Products(new() { Start = before, End = start }).Take(101).ToListAsync(ct);
                if (productNow.Count > 100 || productBefore.Count > 100) complete = false;
                foreach (var id in productNow.Select(x => x.Id).Union(productBefore.Select(x => x.Id)).Take(200))
                {
                    var a = productNow.Find(x => x.Id == id); var b = productBefore.Find(x => x.Id == id);
                    if (Significant(a?.SalesValue ?? 0, b?.SalesValue ?? 0, a?.CompletedOrderCount ?? 0, b?.CompletedOrderCount ?? 0, settings))
                        Add(new("productSales:" + id, "productDemandChange", "warning", "product", id, Name(a?.Product ?? b!.Product), Metrics(a?.SalesValue ?? 0, b!.SalesValue, a?.CompletedOrderCount ?? 0, b.CompletedOrderCount)));
                }
                var customerNow = await reports.Customers(new() { Start = start, End = now }, true).Take(101).ToListAsync(ct);
                var customerBefore = await reports.Customers(new() { Start = before, End = start }, true).Take(101).ToListAsync(ct);
                if (customerNow.Count > 100 || customerBefore.Count > 100) complete = false;
                foreach (var id in customerNow.Select(x => x.Id).Union(customerBefore.Select(x => x.Id)).Take(200))
                {
                    var a = customerNow.Find(x => x.Id == id); var b = customerBefore.Find(x => x.Id == id);
                    if (Significant(a?.SalesValue ?? 0, b?.SalesValue ?? 0, a?.CompletedOrders ?? 0, b?.CompletedOrders ?? 0, settings))
                        Add(new("customerSales:" + id, "customerPurchasingChange", "warning", "customer", id, Name(a?.Customer ?? b!.Customer), Metrics(a?.SalesValue ?? 0, b!.SalesValue, a?.CompletedOrders ?? 0, b.CompletedOrders)));
                }
                diagnostics.Add("observedChangeNotCausalExplanation");
                break;
            default: throw new BusinessException("monitoringInvalidConfiguration");
        }
        // Preferences suppress warning delivery, not the observed state used to clear previous alerts.
        if (!complete) diagnostics.Add("monitoringResultsTruncated");
        return new(findings, complete, diagnostics.Distinct().ToList());
    }
    private static Dictionary<string, decimal> Metrics(decimal current, decimal previous, int currentSamples, int previousSamples)
        => new() { ["currentValue"] = current, ["previousValue"] = previous, ["changePercent"] = decimal.Round((current - previous) / previous * 100, 2), ["currentSamples"] = currentSamples, ["previousSamples"] = previousSamples };
}
