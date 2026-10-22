using System.Text.Json;
using Demo.Api.Errors;

namespace Demo.Api.AI;

// Translate omitted, documented report defaults to the canonical required-field contract.
// Never coerce supplied values, fill one-sided date ranges or expand the offered tool surface.
public static class OllamaReportingArguments
{
    public static string Canonicalize(string name,string arguments,IReadOnlyList<AiToolDefinition> offered,out string? defaults)
    {
        defaults=null;
        var registered=AiToolRegistry.All.SingleOrDefault(t=>t.Name==name);
        if(registered is null||!offered.Any(t=>t.Name==name&&t.Schema==registered.Schema))return arguments;
        using var document=JsonDocument.Parse(arguments,new(){MaxDepth=4});
        if(document.RootElement.ValueKind!=JsonValueKind.Object)return arguments;
        var properties=document.RootElement.EnumerateObject().ToArray();
        using var schema=JsonDocument.Parse(registered.Schema);
        var required=schema.RootElement.GetProperty("required").EnumerateArray().Select(p=>p.GetString()!).ToHashSet(StringComparer.Ordinal);
        var names=properties.Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);
        if(names.Count!=properties.Length||names.Any(p=>!required.Contains(p)))return arguments;
        var values=properties.ToDictionary(p=>p.Name,p=>(object?)p.Value,StringComparer.Ordinal);
        var dates=required.Contains("startDate")&&!names.Contains("startDate")&&!names.Contains("endDate");
        // An explicitly supplied date is never complemented, overwritten or guessed.
        if(required.Contains("startDate")&&names.Contains("startDate")!=names.Contains("endDate"))return arguments;
        var comparison=name=="get_sales_summary"&&!names.Contains("comparePrevious");
        if(!dates&&!comparison)return arguments;
        if(dates){values["startDate"]=null;values["endDate"]=null;}
        if(comparison)values["comparePrevious"]=false;
        var canonical=JsonSerializer.Serialize(values);
        try{AiToolRegistry.Validate(registered,canonical);}
        catch(BusinessException){return arguments;}
        defaults=dates&&comparison?"dates_and_comparison":dates?"dates":"comparison";
        return canonical;
    }
}
