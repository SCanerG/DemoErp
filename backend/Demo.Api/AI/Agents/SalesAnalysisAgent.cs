using System.Text.Json;
using Demo.Api.Errors;
namespace Demo.Api.AI.Agents;
public sealed class SalesAnalysisAgent
{
    public async Task<SalesAnalysis> Analyze(AgentWindow window,Func<AiToolCall,Task<AiToolResult>> read)
    {
        var summary=await read(new(Guid.NewGuid().ToString("N"),"get_sales_summary",JsonSerializer.Serialize(new {startDate=window.Start,endDate=window.End,comparePrevious=true})));
        var products=await read(new(Guid.NewGuid().ToString("N"),"get_top_products",JsonSerializer.Serialize(new {startDate=window.Start,endDate=window.End,limit=10})));
        if(summary.Data is not AiSalesComparison metrics || products.Data is not List<AiProduct> best || best.Count>10)throw new BusinessException("aiToolFailure",503);
        return new(DateTimeOffset.UtcNow,window,metrics,best);
    }
}
