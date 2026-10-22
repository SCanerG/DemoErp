using Demo.Api.Errors;
namespace Demo.Api.AI;

public static class AiToolSelection
{
    // Only retry incomplete explanatory/protocol output before any tool dispatch. Never repair tool arguments.
    public static async Task<AiModelTurn> Complete(Func<IReadOnlyList<AiModelMessage>,Task<AiModelTurn>> generate,
        IReadOnlyList<AiModelMessage> messages,CancellationToken ct,Action<string>? diagnostic=null)
    {
        var tokens=0;
        for(var attempt=0;attempt<2;attempt++) {
            ct.ThrowIfCancellationRequested();
            var input=messages.ToList();
            if(attempt>0)input.Add(new("system","Your previous response was empty or incomplete. Select the appropriate provided READ-ONLY tool using native tool_calls and exactly its required argument schema. Never invent a tool or encode calls in content. If no tool fits, provide a short answer/language JSON clarification. Do not perform writes, SQL or shell commands."));
            try {
                var turn=await generate(input);
                if(turn.TotalTokens is <0)throw new AiProviderException("aiInvalidResponse",502,validationFailure:"invalid_usage");
                if(turn.Calls is {Count:0} && string.IsNullOrWhiteSpace(turn.Answer))throw new AiProviderException("aiInvalidResponse",502,totalTokens:turn.TotalTokens,validationFailure:"empty_answer");
                return turn with {TotalTokens=turn.TotalTokens.HasValue||tokens>0?(turn.TotalTokens??0)+tokens:null};
            }
            catch(AiProviderException ex) when((ex.Code is "aiInvalidResponse" or "aiIncompleteResponse") && (ex.ValidationFailure is "empty_answer" or "answer_schema" or "envelope_json" or "incomplete_thinking" or "invalid_fence" or "output_truncated" or "not_done")) {
                tokens+=ex.TotalTokens??0;diagnostic?.Invoke(ex.ValidationFailure!);
                if(attempt==1)throw new AiProviderException("aiToolSelectionFailed",502,totalTokens:tokens,validationFailure:ex.ValidationFailure);
            }
        }
        throw new BusinessException("aiToolSelectionFailed",502);
    }
}

public static partial class AiReportIntent
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:Şu anda onay bekleyen \d+ sipariş var\.|There are currently \d+ orders awaiting confirmation\.)$")]
    private static partial System.Text.RegularExpressions.Regex PendingCountPattern();
    public static bool IsPendingCount(string text)=>PendingCountPattern().IsMatch(text);
    public static string PendingCount(AiPendingOrders report,string language)=>language=="tr"
        ?$"Şu anda onay bekleyen {report.TotalPendingOrders} sipariş var."
        :$"There are currently {report.TotalPendingOrders} orders awaiting confirmation.";
    // Exact phrases only: ambiguous, date-filtered, compound and write requests still go through the planner.
    private static readonly HashSet<string> PendingQuestions=["kac siparis beklemede","kac siparis bekliyor","bekleyen kac siparis var","kac tane siparis beklemede","kac adet siparis bekliyor","bekleyen siparis sayisi","bekleyen siparisler","bekleyen siparisleri goster","how many orders are pending","how many pending orders are there","show pending orders","pending orders"];
    public static AiToolCall? Pending(string text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>2000)return null;
        var normalized=string.Join(' ',text.Trim().TrimEnd('?','!','.').ToLowerInvariant().Replace('ı','i').Replace('İ','i').Replace('ş','s').Replace('ç','c').Replace('ğ','g').Replace('ö','o').Replace('ü','u').Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        return PendingQuestions.Contains(normalized)?new("server_"+Guid.NewGuid().ToString("N"),"get_pending_orders","{\"limit\":5}"):null;
    }
}
