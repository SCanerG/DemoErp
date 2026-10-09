using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Demo.Api.AI;

public sealed class AiToolExecutor(ReportingService reports, AppDbContext db, IAuthorizationService authorization)
{
    public async Task Recheck(ClaimsPrincipal principal, string policy, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var id) || !int.TryParse(principal.FindFirstValue("sv"), out var version))
            throw new BusinessException("aiUnauthorized", 401);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.IsActive, u.SecurityVersion, u.Role }).SingleOrDefaultAsync(ct);
        if (user is null || !user.IsActive || user.SecurityVersion != version || principal.FindFirstValue("role") != user.Role.ToString())
            throw new BusinessException("aiUnauthorized", 401);
        if (!(await authorization.AuthorizeAsync(principal, null, policy)).Succeeded) throw new BusinessException("aiForbidden", 403);
    }
    private static string Name(string value) => value.Length > 120 ? value[..120] : value;
    public async Task<(AiToolResult Result, AiSource Source)> Execute(AiToolCall call, ClaimsPrincipal principal, CancellationToken ct)
    {
        var tool = AiToolRegistry.Find(call.Name);
        var args = AiToolRegistry.Validate(tool, call.Arguments);
        try
        {
            await Recheck(principal, tool.Policy, ct);
            var filter = new ReportFilter { Start = args.StartDate, End = args.EndDate };
            object data;
            switch (tool.Name)
            {
                case "get_sales_summary":
                    var current = await reports.SalesMetrics(args.StartDate!.Value, args.EndDate!.Value, ct);
                    SalesMetrics? previous = null;
                    DateTimeOffset? previousStart = null;
                    if (args.ComparePrevious)
                    {
                        previousStart = args.StartDate - (args.EndDate - args.StartDate);
                        previous = await reports.SalesMetrics(previousStart!.Value, args.StartDate.Value, ct);
                    }
                    data = new AiSalesComparison(new(current.Sales, current.CompletedOrders, current.AverageOrderValue),
                        previous is null ? null : new(previous.Sales, previous.CompletedOrders, previous.AverageOrderValue),
                        previous is null ? null : current.Sales - previous.Sales,
                        previous is null || previous.Sales == 0 ? null : decimal.Round((current.Sales - previous.Sales) / previous.Sales * 100, 2),
                        previousStart, previous is null ? null : args.StartDate, await reports.LegacyCompletedCount(ct));
                    break;
                case "get_top_products":
                    data = (await reports.Products(filter).Take(args.Limit).ToListAsync(ct)).Select(p => new AiProduct(Name(p.Product), p.QuantitySold, p.SalesValue, p.CompletedOrderCount, p.CurrentStock)).ToList(); break;
                case "get_top_customers":
                    data = (await reports.Customers(filter, true).Take(args.Limit).ToListAsync(ct)).Select(c => new AiCustomer(
                        "Customer-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.Id.ToString())))[..12], c.CompletedOrders, c.SalesValue, c.AverageOrderValue)).ToList(); break;
                case "get_order_status_summary": data = await reports.Statuses(filter, ct); break;
                case "get_low_stock_products":
                case "get_out_of_stock_products":
                    data = (await reports.Inventory(new() { StockStatus = tool.Name == "get_low_stock_products" ? "Low" : "Out", Sort = "quantityOnHand", Descending = false })
                        .Take(args.Limit).ToListAsync(ct)).Select(i => new AiStock(Name(i.Product), i.QuantityOnHand, i.MinimumStockLevel, i.StockStatus)).ToList(); break;
                case "get_inventory_overview":
                    var overview = await reports.InventoryOverview(ct);
                    data = new AiInventoryOverview(overview.TotalProducts, overview.InStockProducts, overview.LowStockProducts, overview.OutOfStockProducts); break;
                case "get_pending_orders":
                    var pending = await reports.PendingOrders(args.Limit, ct);
                    data = new AiPendingOrders(pending.TotalPendingOrders, pending.Orders.Select(o => new AiPendingOrder(Name(o.OrderNumber), o.OrderDate, o.TotalAmount)).ToList()); break;
                case "get_stock_risk":
                    data = (await reports.StockCoverage(filter, args.Limit, ct)).Select(r => new AiStockRisk(Name(r.Product), r.QuantityOnHand, r.RecentCompletedQuantity,
                        decimal.Round(r.DailyAverage, 4), decimal.Round(r.EstimatedDaysRemaining, 2))).ToList(); break;
                default: throw new BusinessException("aiUnknownTool");
            }
            var basis = tool.Name switch { "get_order_status_summary" => "OrderDate period; current statuses",
                "get_inventory_overview" or "get_pending_orders" or "get_low_stock_products" or "get_out_of_stock_products" => "Current snapshot",
                "get_stock_risk" => "CompletedAt historical demand; current stock; constant-demand estimate",
                _ => "CompletedAt historical sales; legacy without CompletedAt excluded; any stock is current" };
            var hasData = data switch { AiSalesComparison s => s.Current.CompletedOrders > 0 || s.Previous?.CompletedOrders > 0,
                AiInventoryOverview i => i.TotalProducts > 0, AiPendingOrders p => p.TotalPendingOrders > 0,
                List<StatusCount> statuses => statuses.Any(s => s.Count > 0), System.Collections.ICollection items => items.Count > 0, _ => false };
            return (new(tool.Name, DateTimeOffset.UtcNow, args.StartDate, args.EndDate, basis, "USD", hasData, data),
                new("report", tool.Name, tool.Label, JsonSerializer.SerializeToElement(args, AiAssistantService.Json), tool.ReportPath));
        }
        catch (BusinessException ex) when (ex.Code == "invalidReportFilters") { throw new BusinessException("aiInvalidArguments"); }
        catch (BusinessException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new BusinessException("aiToolFailure", 503); }
    }
}
