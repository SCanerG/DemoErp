using System.Text.Json;
using Demo.Api.Errors;
namespace Demo.Api.AI.Agents;
public static class AgentPlanner
{
    public static readonly AiToolDefinition RouteTool=new("route_erp_request",
        "Select one fixed read-only ERP workflow. Extract customer/product names and integer quantities ONLY from the user's text; no IDs or prices. Missing quantities are null. Use clarification for unsupported requests.",
        """{"type":"object","properties":{"kind":{"type":"string","enum":["inventory","sales","order","collaboration","clarification"]},"period":{"type":"string","enum":["last30days","last3months","thismonth","previousmonth"]},"customer":{"type":["string","null"]},"lines":{"type":"array","maxItems":10,"items":{"type":"object","properties":{"product":{"type":"string"},"quantity":{"type":["integer","null"]}},"required":["product","quantity"],"additionalProperties":false}}},"required":["kind","period","customer","lines"],"additionalProperties":false}""",
        "BusinessRead","Route request","");
    public static readonly AiToolDefinition OrderRouteTool=RouteTool with {
        Description="Extract the requested ORDER DRAFT only, or request clarification. Copy customer and product names from the user. No sales/inventory analysis, IDs, prices, SQL or writes.",
        Schema=RouteTool.Schema.Replace("[\"inventory\",\"sales\",\"order\",\"collaboration\",\"clarification\"]","[\"order\",\"clarification\"]",StringComparison.Ordinal)
    };
    private static void Shape(JsonElement value,params string[] keys) {
        if(value.ValueKind!=JsonValueKind.Object)throw new FormatException();
        var names=value.EnumerateObject().Select(p=>p.Name).ToArray();
        if(names.Length!=keys.Length || !names.ToHashSet().SetEquals(keys))throw new FormatException();
    }
    public static AgentPlan Parse(AiModelTurn turn,string mode)
    {
        try {
            if(turn.Calls is null || turn.Answer is not null || turn.Language is not null || turn.Calls.Count!=1 || turn.Calls[0].Name!=RouteTool.Name || turn.Calls[0].Arguments.Length>4000)throw new FormatException();
            using var doc=JsonDocument.Parse(turn.Calls[0].Arguments,new JsonDocumentOptions {MaxDepth=6});var r=doc.RootElement;
            Shape(r,"kind","period","customer","lines");
            var kind=r.GetProperty("kind").GetString()!;var period=r.GetProperty("period").GetString()!;var customer=r.GetProperty("customer").GetString();
            if(kind is not ("inventory" or "sales" or "order" or "collaboration" or "clarification") || !AgentRouting.Period(period)||period=="auto"
                || customer?.Length>150 || mode=="order"&&kind is not ("order" or "clarification"))throw new FormatException();
            var lines=r.GetProperty("lines");if(lines.ValueKind!=JsonValueKind.Array||lines.GetArrayLength()>10)throw new FormatException();
            var items=new List<DraftIntentLine>();
            foreach(var line in lines.EnumerateArray()) {
                Shape(line,"product","quantity");var name=line.GetProperty("product").GetString();var q=line.GetProperty("quantity");
                if(string.IsNullOrWhiteSpace(name)||name.Length>120)throw new FormatException();
                int? quantity=q.ValueKind==JsonValueKind.Null ? null : q.GetInt32();
                if(quantity is <1 or >10000)throw new FormatException();items.Add(new(name.Trim(),quantity));
            }
            if(kind!="order"&&(items.Count!=0||customer is not null))throw new FormatException();
            return new(kind,period,customer?.Trim(),items);
        }
        catch(Exception ex)when(ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {throw new BusinessException("aiInvalidResponse",502);}
    }
}
