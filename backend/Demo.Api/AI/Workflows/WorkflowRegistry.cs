using System.Text.RegularExpressions;
using Demo.Api.Errors;

namespace Demo.Api.AI.Workflows;

public sealed record WorkflowNodeType(string Type, string Category, bool Pure, bool Agent = false);
public static partial class WorkflowRegistry
{
    public static readonly IReadOnlyList<WorkflowNodeType> All = new[] {
        new WorkflowNodeType("trigger.manual","trigger",true), new("trigger.schedule","trigger",true), new("trigger.inventory","trigger",true), new("trigger.orderStatus","trigger",true), new("trigger.overduePurchase","trigger",true),
        new("condition.numeric","condition",true), new("condition.status","condition",true), new("condition.date","condition",true), new("condition.boolean","condition",true),
        new("agent.inventory","agent",true,true), new("agent.sales","agent",true,true), new("agent.order","agent",true,true), new("agent.knowledge","agent",true,true),
        new("action.notify","action",false), new("action.report","action",true), new("action.propose","action",false), new("action.requestApproval","action",false), new("action.executeApproved","action",true),
        new("control.end","control",true), new("control.branch","condition",true), new("control.retry","control",true)
    };
    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_-]{0,39}$")] private static partial Regex Identifier();
    public static bool Pure(string type) => All.Single(t => t.Type == type).Pure && type is not "agent.order";
    public static bool Branch(string type) => All.Single(t => t.Type == type).Category == "condition";
    public static List<WorkflowNode> Validate(WorkflowGraph graph)
    {
        void Reject([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(true)] bool bad) { if (bad) throw new BusinessException("workflowInvalidGraph"); }
        Reject(graph is null || graph!.Nodes is null || graph.Edges is null || graph.Policy is null);
        Reject(graph!.SchemaVersion != 1 || graph.Nodes.Count is < 2 or > 25 || graph.Edges.Count is < 1 or > 40 || WorkflowModel.Write(graph).Length > 60000);
        var policy = graph.Policy;
        Reject(policy.TimeoutSeconds is < 10 or > 120 || policy.Retries is < 0 or > 2 || policy.MaxAgents is < 0 or > 3 || policy.MaxTools is < 1 or > 10);
        Reject(graph.Nodes.Any(n => n is null || n.Config is null || n.Id is null || !Identifier().IsMatch(n.Id) || !All.Any(t => t.Type == n.Type) || !double.IsFinite(n.X) || !double.IsFinite(n.Y) || Math.Abs(n.X)>100000 || Math.Abs(n.Y)>100000));
        Reject(graph.Nodes.Select(n => n.Id).Distinct().Count() != graph.Nodes.Count || graph.Edges.Any(e => e is null || e.Id is null || !Identifier().IsMatch(e.Id)) || graph.Edges.Select(e => e.Id).Distinct().Count() != graph.Edges.Count);
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        Reject(graph.Edges.Any(e => !nodes.ContainsKey(e.Source) || !nodes.ContainsKey(e.Target) || e.Source == e.Target || e.When is not ("always" or "true" or "false")));
        Reject(graph.Edges.GroupBy(e => new { e.Source, e.Target, e.When }).Any(g => g.Count() > 1));
        var roots = graph.Nodes.Where(n => n.Type.StartsWith("trigger.", StringComparison.Ordinal)).ToList();
        Reject(roots.Count != 1); var root = roots[0];
        Reject(graph.Nodes.Count(n => n.Type.StartsWith("agent.", StringComparison.Ordinal)) > policy.MaxAgents);
        Reject(new[]{"agent.order","action.propose","action.requestApproval","action.executeApproved"}.Any(type=>graph.Nodes.Count(n=>n.Type==type)>1));
        foreach (var n in graph.Nodes)
        {
            var incoming = graph.Edges.Where(e => e.Target == n.Id).ToArray(); var outgoing = graph.Edges.Where(e => e.Source == n.Id).ToArray(); var c = n.Config;
            Reject(n == root ? incoming.Length != 0 : incoming.Length == 0);
            // v1: a single typed input per operation; only End nodes may join branches.
            Reject(n.Type != "control.end" && incoming.Length > 1);
            Reject(n.Type == "control.end" ? outgoing.Length != 0 : outgoing.Length == 0);
            Reject(Branch(n.Type) ? outgoing.Length != 2 || !outgoing.Select(e => e.When).ToHashSet().SetEquals(["true","false"]) : outgoing.Any(e => e.When != "always"));
            Reject(c.Field is not ("count" or "stock" or "sold" or "sales" or "evidence" or "approved") || c.Comparison is not ("lt" or "lte" or "eq" or "gte" or "gt") || c.Status is not ("Pending" or "Confirmed" or "Completed" or "Cancelled")
                || c.Threshold is < 0 or > 1000000 || c.Number is < -1000000000 or > 1000000000 || c.DayOfWeek is < 0 or > 6 || c.HourUtc is < 0 or > 23 || c.MinuteUtc is < 0 or > 59 || c.PeriodDays is < 1 or > 90 || c.Quantity is < 1 or > 10000 || c.Retries is < 0 or > 2 || c.Question is null || c.Question.Length > 1000 || c.Language is not("tr" or "en"));
            Reject(n.Type == "condition.numeric" && c.Field is not ("count" or "stock" or "sold" or "sales"));
            Reject(n.Type == "condition.boolean" && c.Field is not ("evidence" or "approved"));
            Reject(n.Type == "condition.date" && c.Date is null);
            Reject(n.Type == "agent.order" && (c.CustomerId is null || c.ProductId is null || c.CustomerId == Guid.Empty || c.ProductId == Guid.Empty));
            Reject(n.Type == "agent.knowledge" && string.IsNullOrWhiteSpace(c.Question));
            string? parent = incoming.Length == 1 ? nodes[incoming[0].Source].Type : null;
            Reject(n.Type == "action.propose" && parent != "agent.order");
            Reject(n.Type == "action.requestApproval" && parent != "action.propose");
            Reject(n.Type == "action.executeApproved" && parent != "action.requestApproval");
            Reject(n.Type == "control.retry" && (outgoing.Length != 1 || !Pure(nodes[outgoing[0].Target].Type)));
        }
        var degree = graph.Nodes.ToDictionary(n => n.Id, n => graph.Edges.Count(e => e.Target == n.Id)); var ready = new Queue<string>([root.Id]); var result = new List<WorkflowNode>();
        while (ready.TryDequeue(out var id)) { result.Add(nodes[id]); foreach (var e in graph.Edges.Where(e => e.Source == id)) if (--degree[e.Target] == 0) ready.Enqueue(e.Target); }
        Reject(result.Count != graph.Nodes.Count || !graph.Nodes.Any(n => n.Type == "control.end")); return result;
    }
    public static DateTimeOffset NextWeekly(WorkflowConfig c, DateTimeOffset after)
    {
        var utc = after.ToUniversalTime(); var next = new DateTimeOffset(utc.Year, utc.Month, utc.Day,c.HourUtc,c.MinuteUtc,0,TimeSpan.Zero);
        var days = (c.DayOfWeek - (int)next.DayOfWeek + 7) % 7; next = next.AddDays(days); return next <= utc ? next.AddDays(7) : next;
    }
    public static bool Compare(decimal value, decimal expected, string comparison) => comparison switch { "lt"=>value<expected,"lte"=>value<=expected,"eq"=>value==expected,"gte"=>value>=expected,"gt"=>value>expected,_=>throw new BusinessException("workflowInvalidGraph") };
    public static WorkflowGraph Template(string key)
    {
        List<WorkflowNode> nodes = key switch {
            "criticalInventory" => [new("start","trigger.inventory",40,80,new()),new("inventory","agent.inventory",300,80,new()),new("notify","action.notify",560,80,new()),new("end","control.end",820,80,new())],
            "overduePurchase" => [new("start","trigger.overduePurchase",40,80,new()),new("notify","action.notify",300,80,new()),new("end","control.end",560,80,new())],
            "weeklySales" => [new("start","trigger.schedule",40,80,new()),new("sales","agent.sales",300,80,new(PeriodDays:7)),new("report","action.report",560,80,new()),new("notify","action.notify",820,80,new()),new("end","control.end",1080,80,new())],
            "inventoryPolicy" => [new("start","trigger.manual",40,80,new()),new("inventory","agent.inventory",300,80,new()),new("knowledge","agent.knowledge",560,80,new(Question:"What is the inventory replenishment policy?",Optional:true)),new("notify","action.notify",820,80,new()),new("end","control.end",1080,80,new())],
            "blank" => [new("start","trigger.manual",40,80,new()),new("end","control.end",300,80,new())],
            _ => throw new BusinessException("workflowInvalidGraph")
        };
        return new(1,nodes,nodes.Zip(nodes.Skip(1),(a,b)=>new WorkflowEdge("edge_"+a.Id,a.Id,b.Id)).ToList(),new());
    }
}
