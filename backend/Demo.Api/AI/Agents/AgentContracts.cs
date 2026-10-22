using System.Text.Json;
using Demo.Api.Errors;
namespace Demo.Api.AI.Agents;

public sealed record AgentStartRequest(string Mode = "auto");
public sealed record AgentInput(string Message, string Language, string Period = "auto");
public sealed record DraftIntentLine(string Product, int? Quantity);
public sealed record AgentPlan(string Kind, string Period, string? Customer, List<DraftIntentLine> Lines);
public sealed record AgentWindow(DateTimeOffset Start, DateTimeOffset End);
public sealed record ReplenishmentRow(Guid ProductId, string Product, int CurrentStock, int MinimumStock,
    long CompletedQuantity, decimal DailyAverage, int? TargetStock, int? SuggestedQuantity, string Basis);
public sealed record InventoryAnalysis(DateTimeOffset RetrievedAt, AgentWindow SalesPeriod, int MatchingProducts, bool Truncated, List<ReplenishmentRow> Products);
public sealed record SalesAnalysis(DateTimeOffset RetrievedAt, AgentWindow Period, AiSalesComparison Summary, List<AiProduct> BestSellers);
public sealed record DraftCandidate(Guid Id, string Name);
public sealed record DraftClarification(string Field, string Query, string Reason, List<DraftCandidate> Candidates);
public sealed record ResolvedDraftLine(Guid ProductId, string Product, int Quantity, decimal UnitPrice, decimal LineTotal, int AvailableStock, int Shortage);
public sealed record ResolvedOrderDraft(Guid? CustomerId, string? Customer, Guid? WarehouseId, string? Warehouse,
    List<ResolvedDraftLine> Lines, decimal? Total, string Currency, List<DraftClarification> Clarifications,
    bool NotSubmitted = true, bool Persisted = false, bool RequiresHumanReview = true);
public sealed record AgentResult(Guid ExecutionId, string Intent, string Answer, string Language, bool IsMock,
    InventoryAnalysis? Inventory, SalesAnalysis? Sales, ResolvedOrderDraft? OrderDraft, List<AiSource> Sources, string ResponseKind = "model", List<ProposalView>? Proposals = null, List<string>? MissingPurchaseReferences = null, Knowledge.KnowledgeAnswer? Knowledge = null);
public sealed record AgentStepStatus(int Sequence, string Agent, string Outcome, long DurationMs, DateTimeOffset CreatedAt);
public sealed record AgentRunStatus(string Mode, AiExecutionStatus Execution, List<AgentStepStatus> Steps, Guid InitiatingUserId = default, DateTimeOffset? CompletedAt = null, Guid? RetryOf = null, DateTimeOffset? StartedAt = null);
public sealed record AgentRunSummary(Guid Id, string Mode, string Outcome, string Provider, string Model, DateTimeOffset CreatedAt);
public sealed record AgentProviderStatus(bool Enabled, bool Configured, string Provider, string Model, bool IsMock, string Health, int ContextWindow,
    int MaxSteps = 5, int MaxTools = 6, int MaxModelCalls = 3, int MaxContextCharacters = 28000);

public sealed class AgentBudget(int maxSteps = 5, int maxTools = 6, int maxModels = 3)
{
    public int Steps { get; private set; }
    public int Tools { get; private set; }
    public int Models { get; private set; }
    public void Step() { if (++Steps > maxSteps) throw new BusinessException("aiAgentLimit", 400); }
    public void Tool() { if (++Tools > maxTools) throw new BusinessException("aiAgentLimit", 400); }
    public void Model() { if (++Models > maxModels) throw new BusinessException("aiAgentLimit", 400); }
}
public static class AgentRouting
{
    public static bool Mode(string value) => value is "auto" or "inventory" or "sales" or "order" or "collaboration" or "procurement" or "knowledge" or "inventoryknowledge";
    public static bool Period(string value) => value is "auto" or "last30days" or "last3months" or "thismonth" or "previousmonth";
    public static string? Simple(string input)
    {
        var s=input.ToLowerInvariant();
        var knowledge=new[]{"policy","procedure","manual","documentation","how do i","how to ","politika","prosedür","kılavuz","doküman","nasıl"}.Any(s.Contains);
        if(knowledge)return new[]{"inventory","stock","stok","envanter"}.Any(s.Contains)?"inventoryknowledge":"knowledge";
        if (new[]{"satın alma", "satin alma", "purchase order", "purchasing order"}.Any(s.Contains)) return "procurement";
        if (s.Contains("draft") || s.Contains("taslak") || s.Contains("taslağ") || s.Contains("taslag")) return "order";
        var inventory=new[]{"inventory","stock","replenish","stok","stoğ","envanter","ikmal","tedarik"}.Any(s.Contains);
        var sales=new[]{"sales","selling","satış","satis"}.Any(s.Contains);
        return (inventory,sales) switch { (true,true)=>"collaboration", (true,false)=>"inventory", (false,true)=>"sales", _=>null };
    }
    public static string DetectPeriod(string text) {
        var s=text.ToLowerInvariant();
        if(s.Contains("last month")||s.Contains("geçen ay"))return "previousmonth";
        if(s.Contains("this month")||s.Contains("bu ay"))return "thismonth";
        return s.Contains("30") ? "last30days" : "last3months";
    }
    public static AgentWindow Window(string period, DateTimeOffset now) {
        var end=now.ToUniversalTime(); var month=new DateTimeOffset(end.Year,end.Month,1,0,0,0,TimeSpan.Zero);
        return period switch {
            "last30days"=>new(end.AddDays(-30),end), "last3months"=>new(end.AddMonths(-3),end),
            "thismonth"=>new(month,end), "previousmonth"=>new(month.AddMonths(-1),month), _=>throw new BusinessException("aiInvalidRequest") };
    }
}
public static class AgentTools
{
    public const string Inventory="analyze_replenishment";
    public const string Order="resolve_order_draft";
    public static bool Known(string name)=>name is Inventory or Order or "prepare_purchase_proposals" or "retrieve_knowledge";
}
