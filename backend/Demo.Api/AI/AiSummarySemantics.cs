using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Demo.Api.AI;

public static partial class AiSummarySemantics
{
    [GeneratedRegex(@"(?:\b(?:TL|TRY|EUR|GBP)\b|₺|€|£|türk\s+liras[ıi])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex ForeignCurrency();
    [GeneratedRegex(@"(?:stokta|stoğu|stoku|stok|stock(?:\s+is)?)\s*[:=]?\s*(?<quantity>\d+)\b|\b(?<quantity>\d+)\s+(?:adet\s+)?stok(?:ta|a|\b)|\b(?<quantity>\d+)\s+units?\s+(?:in\s+)?stock",RegexOptions.IgnoreCase)]
    private static partial Regex StockQuantity();
    [GeneratedRegex(@"^\[[^\]]+\]\s*")]
    private static partial Regex Prefix();
    public static void Validate(string answer,string facts,string language)
    {
        AiVerifiedNumbers.Validate(answer,facts,language);
        // This ERP's reporting and order contracts use USD; translation must never convert the currency.
        if(ForeignCurrency().IsMatch(answer))throw new AiProviderException("aiInvalidResponse",502,validationFailure:"currency_mismatch");
        using var document=JsonDocument.Parse(facts);var stocks=new Dictionary<string,HashSet<int>>();Collect(document.RootElement,stocks);
        if(stocks.Count==0)return;
        string[] context=[];
        foreach(var sentence in Regex.Split(answer,@"[.!?\n]+")) {
            var normalized=sentence.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
            var names=stocks.Keys.Where(normalized.Contains).ToArray();
            if(names.Length>0)context=names;
            var claims=StockQuantity().Matches(sentence);
            if(claims.Count==0)continue;
            if(context.Length!=1||stocks[context[0]].Count!=1)throw new AiProviderException("aiInvalidResponse",502,validationFailure:"unattributed_stock_claim");
            var expected=stocks[context[0]].Single();
            foreach(Match claim in claims)if(!int.TryParse(claim.Groups["quantity"].Value,out var value)||value!=expected)
                throw new AiProviderException("aiInvalidResponse",502,validationFailure:"stock_claim_mismatch");
        }
    }
    private static void Collect(JsonElement element,Dictionary<string,HashSet<int>> stocks)
    {
        if(element.ValueKind==JsonValueKind.Array){foreach(var item in element.EnumerateArray())Collect(item,stocks);return;}
        if(element.ValueKind!=JsonValueKind.Object)return;
        if(element.TryGetProperty("product",out var product)&&product.ValueKind==JsonValueKind.String) {
            var name=Prefix().Replace(product.GetString()??"","").Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
            foreach(var key in new[]{"currentStock","quantityOnHand","availableStock"})if(name.Length>=3&&element.TryGetProperty(key,out var stock)&&stock.TryGetInt32(out var count)) {
                if(!stocks.TryGetValue(name,out var values))stocks[name]=values=[];
                values.Add(count);
            }
        }
        foreach(var property in element.EnumerateObject())Collect(property.Value,stocks);
    }
}
