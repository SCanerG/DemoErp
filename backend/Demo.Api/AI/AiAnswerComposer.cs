using Demo.Api.Errors;
namespace Demo.Api.AI;

public sealed class AiUnsafeOutputException() : BusinessException("aiInvalidResponse",502);

public static class AiAnswerComposer
{
    public const string TurkishFallback="Doğrulanmış ERP özeti (otomatik şablon; yapay zekâ açıklaması üretilemedi).";
    public const string EnglishFallback="Verified ERP summary (automatic template; an AI explanation could not be generated).";
    public static string Kind(string text)=>text.StartsWith(TurkishFallback,StringComparison.Ordinal)||text.StartsWith(EnglishFallback,StringComparison.Ordinal)?"fallback":
        text.StartsWith("İstenen raporlarda eşleşen kayıt bulunamadı.",StringComparison.Ordinal)||text.StartsWith("No matching records were found in the requested reports.",StringComparison.Ordinal)
        ||text.StartsWith("Yalnızca izinli satış, sipariş ve stok raporlarını okuyabilirim.",StringComparison.Ordinal)||text.StartsWith("I can only read approved sales, order and inventory reports.",StringComparison.Ordinal)||AiReportIntent.IsPendingCount(text)?"deterministic":"model";
    // Recovery is exclusively for final explanatory text. Tools are never retried/replayed here.
    public static async Task<AiModelTurn> Complete(Func<bool,Task<AiModelTurn>> generate,string language,string key,
        Func<string> trustedFallback,CancellationToken ct,Action<string>? diagnostic=null,Action<string>? validateFacts=null)
    {
        int knownTokens=0;
        for(var attempt=0;attempt<2;attempt++) {
            ct.ThrowIfCancellationRequested();
            int observedTokens=0;
            try {
                var turn=await generate(attempt==1);
                observedTokens=turn.TotalTokens??0;
                // Never repair or template over a tool request or attempted secret disclosure.
                if(turn.Calls is null||turn.Calls.Count>0||AiOutputValidator.ContainsSecret(turn.Answer??"",key))
                    throw new AiUnsafeOutputException();
                AiOutputValidator.Validate(turn,language,key);
                validateFacts?.Invoke(turn.Answer!);
                return turn with { TotalTokens=turn.TotalTokens.HasValue||knownTokens>0?(turn.TotalTokens??0)+knownTokens:null };
            }
            catch(BusinessException ex) when(ex is not AiUnsafeOutputException && ex.Code is "aiInvalidResponse" or "aiIncompleteResponse") {
                knownTokens+=(ex as AiProviderException)?.TotalTokens??observedTokens;
                diagnostic?.Invoke((ex as AiProviderException)?.ValidationFailure??"final_contract");
            }
        }
        ct.ThrowIfCancellationRequested();
        var answer=trustedFallback();
        if(AiOutputValidator.ContainsSecret(answer,key)||string.IsNullOrWhiteSpace(answer)||answer.Length>8000)throw new BusinessException("aiInvalidResponse",502);
        return new(answer,language,[],knownTokens,"fallback");
    }
}

// Conservative value check, in addition to the prompt: new numerical values require a verified source.
// This does not prove that a model associated a source value with the correct entity.
public static partial class AiVerifiedNumbers
{
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\p{L}\d])[-+]?\d+(?:[.,]\d+)*(?![\p{L}\d])")]
    private static partial System.Text.RegularExpressions.Regex Numbers();
    public static void Validate(string answer,string verifiedJson,string language)
    {
        var values=new HashSet<decimal>();
        using var document=System.Text.Json.JsonDocument.Parse(verifiedJson);
        Collect(document.RootElement,values);
        var culture=System.Globalization.CultureInfo.GetCultureInfo(language=="tr"?"tr-TR":"en-US");
        foreach(System.Text.RegularExpressions.Match match in Numbers().Matches(answer)) {
            var value=Value(match.Value,culture);
            if(!value.HasValue||!values.Contains(value.Value))throw new AiProviderException("aiInvalidResponse",502,validationFailure:"unverified_number");
        }
    }
    private static void Collect(System.Text.Json.JsonElement element,HashSet<decimal> values)
    {
        switch(element.ValueKind) {
            case System.Text.Json.JsonValueKind.Number:if(element.TryGetDecimal(out var value))values.Add(value);break;
            case System.Text.Json.JsonValueKind.Array:foreach(var item in element.EnumerateArray())Collect(item,values);break;
            case System.Text.Json.JsonValueKind.Object:foreach(var property in element.EnumerateObject())Collect(property.Value,values);break;
            case System.Text.Json.JsonValueKind.String:
                foreach(System.Text.RegularExpressions.Match match in Numbers().Matches(element.GetString()??""))
                    if(Value(match.Value,System.Globalization.CultureInfo.InvariantCulture) is decimal number)values.Add(number);
                break;
        }
    }
    private static decimal? Value(string text,System.Globalization.CultureInfo culture)=>decimal.TryParse(text,System.Globalization.NumberStyles.Number,culture,out var value)?value:null;
}
public static class AiTrustedSummary
{
    public static string Tools(IReadOnlyList<AiToolResult> facts,string language)
    {
        var tr=language=="tr";var lines=new List<string>{tr?AiAnswerComposer.TurkishFallback:AiAnswerComposer.EnglishFallback};
        foreach(var fact in facts) {
            switch(fact.Data) {
                case List<AiStockRisk> risks:
                    lines.Add(tr?"Stok hedefleri minimum seviyeyi aşma veya 14 günlük sabit geçmiş talebi karşılama politikasına dayanır; tahmin değildir. Satış olmayan ürünler bu raporda yer almaz.":"Stock targets clear the minimum level or cover 14 days of constant historical demand; these are policy suggestions, not forecasts. Products without completed sales are excluded.");
                    foreach(var r in risks.Take(10))lines.Add(tr?$"{r.Product}: mevcut stok {r.QuantityOnHand}, dönem satışı {r.RecentCompletedQuantity}, hedef stok {r.TargetStock?.ToString()??"belirsiz"}, önerilen ikmal {r.SuggestedQuantity?.ToString()??"belirsiz"} adet.":$"{r.Product}: current stock {r.QuantityOnHand}, period sales {r.RecentCompletedQuantity}, target stock {r.TargetStock?.ToString()??"unknown"}, suggested replenishment {r.SuggestedQuantity?.ToString()??"unknown"} units.");break;
                case List<AiStock> rows:
                    foreach(var r in rows.Take(5))lines.Add(tr?$"{r.Product} ürününün mevcut stoğu {r.QuantityOnHand} adet, minimum stok seviyesi {r.MinimumStockLevel} adettir. Stok ihtiyacını kaynak rapordan inceleyin.":$"{r.Product} currently has {r.QuantityOnHand} units; its minimum stock level is {r.MinimumStockLevel}. Review the source report for replenishment.");break;
                case AiInventoryOverview i:lines.Add(tr?$"Stok raporunda {i.TotalProducts} ürün bulunuyor. {i.LowStockProducts} ürün düşük stokta, {i.OutOfStockProducts} ürünün stoğu tükenmiş.":$"The inventory report contains {i.TotalProducts} products. {i.LowStockProducts} have low stock and {i.OutOfStockProducts} are out of stock.");break;
                case AiSalesComparison s:lines.Add(tr?$"Seçili dönemde {s.Current.CompletedOrders} sipariş tamamlandı. Kayıtlı satış tutarı {s.Current.Sales} USD.":$"In the selected period, {s.Current.CompletedOrders} orders were completed, with recorded sales of {s.Current.Sales} USD.");break;
                case AiPendingOrders p:lines.Add(tr?$"Şu anda onay bekleyen {p.TotalPendingOrders} sipariş var.":$"There are currently {p.TotalPendingOrders} orders awaiting confirmation.");break;
            }
        }
        if(lines.Count==1)lines.Add(tr?"İzinli rapor verileri alındı. Ayrıntıları kaynak raporlarda inceleyin.":"Authorized report data was retrieved. Review the source reports for details.");
        return string.Join("\n\n",lines).TruncateSummary();
    }
    private static string TruncateSummary(this string text)=>text.Length>7800?text[..7800]:text;
}
