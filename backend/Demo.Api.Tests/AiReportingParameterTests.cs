using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.Errors;

namespace Demo.Api.Tests;

public sealed partial class AiTests
{
    [Fact]
    public async Task Stock_risk_uses_the_shared_server_policy_and_fallback_keeps_verified_replenishment()
    {
        await Seed();
        var fake=new FakeModel();
        fake.Turns.Enqueue(new(null,null,[Call("get_stock_risk","{"+Dates+",\"limit\":10}")]));
        fake.Turns.Enqueue(Final("tr","Stok verileri kaynak raporda gösteriliyor."));
        using var app=App(fake,new(){Enabled=true,Provider="Ollama",AllowDevelopmentMock=true,Model="qwen3:8b"});
        var session=await Session();using var client=Authorized(app,session.Token);
        var response=await Chat(client,"Dönem satışlarına göre stok ve ikmal öner.","tr");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var payload=Assert.Single(fake.Seen,m=>m.Role=="tool");
        var facts=JsonSerializer.Deserialize<AiToolResult>(payload.Text,AiAssistantService.Json)!;
        var row=Assert.Single(((JsonElement)facts.Data).EnumerateArray());
        // Four completed units over three days: rounded daily demand 1.3333, 14-day target 19, stock 3 => 16.
        Assert.Equal(5,row.GetProperty("minimumStockLevel").GetInt32());Assert.Equal(19,row.GetProperty("targetStock").GetInt32());Assert.Equal(16,row.GetProperty("suggestedQuantity").GetInt32());
        var summary=AiTrustedSummary.Tools([facts with{Data=new List<AiStockRisk>{new("Test ürün",3,4,1.3333m,2.25m,5,19,16)}}],"tr");
        Assert.Contains("hedef stok 19",summary);Assert.Contains("önerilen ikmal 16",summary);Assert.StartsWith(AiAnswerComposer.TurkishFallback,summary);
    }
    private sealed class ReportingShapeModel(IAiModelClient inner) : IAiModelClient
    {
        public async Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages,IReadOnlyList<AiToolDefinition> tools,CancellationToken ct)
        {
            var turn=await inner.Complete(messages,tools,ct);
            foreach(var call in turn.Calls)
            {
                using var document=JsonDocument.Parse(call.Arguments);
                var known=new[]{"startDate","endDate","comparePrevious","limit"};
                Console.WriteLine("REPORT_ARGUMENT_SHAPE "+JsonSerializer.Serialize(new{tool=AiToolRegistry.All.Any(t=>t.Name==call.Name)?call.Name:"unknown",fields=document.RootElement.EnumerateObject().Where(p=>known.Contains(p.Name)).Select(p=>new{name=p.Name,type=p.Value.ValueKind.ToString()}),unknownFields=document.RootElement.EnumerateObject().Count(p=>!known.Contains(p.Name))}));
            }
            return turn;
        }
    }

    [Theory]
    [InlineData("En çok satılan 5 ürünü göster.","get_top_products")]
    [InlineData("Son 30 günün satışlarını özetle.","get_sales_summary")]
    [InlineData("son 30 günün satışlarına göre önerilen stok ve ikmal miktarını göster","get_stock_risk")]
    public async Task Live_Ollama_reporting_suggestions_execute_authorized_reports(string question,string expectedTool)
    {
        if(Environment.GetEnvironmentVariable("REPORT_ARGUMENT_LIVE_SMOKE")!="true")Assert.Skip("Opt in with REPORT_ARGUMENT_LIVE_SMOKE=true; no local model request made.");
        var settings=new AiAssistantOptions{Enabled=true,Provider="Ollama",AllowDevelopmentMock=true,Model="qwen3:8b",TimeoutSeconds=120,MaxOutputTokens=2000,MaxRetries=0};
        using var http=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=Timeout.InfiniteTimeSpan};
        var model=new ReportingShapeModel(new OllamaModelClient(settings,http));
        using var app=App(model,settings);var session=await Session();using var client=Authorized(app,session.Token);client.Timeout=TimeSpan.FromSeconds(150);
        var response=await Chat(client,question,"tr");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var answer=await response.Content.ReadFromJsonAsync<AiChatResponse>(Ct);Assert.Contains(answer!.Sources,s=>s.Name==expectedTool);
        Console.WriteLine("LIVE_REPORT_VERIFIED "+JsonSerializer.Serialize(new{tool=expectedTool,provider="Ollama",model=settings.Model,kind=answer.ResponseKind,sources=answer.Sources.Count}));
    }
}

public sealed class OllamaReportingArgumentsTests
{
    private sealed class ReportTransport(string name,string arguments):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"done\":true,\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\""+name+"\",\"arguments\":"+arguments+"}}]}}")});
    }
    [Theory]
    [InlineData("get_top_products","{\"limit\":5}")]
    [InlineData("get_sales_summary","{\"startDate\":null,\"endDate\":null}")]
    public async Task Actual_Ollama_adapter_maps_native_omissions_before_returning_the_provider_contract(string name,string arguments)
    {
        using var http=new HttpClient(new ReportTransport(name,arguments));var model=new OllamaModelClient(OllamaTests.Settings,http);
        var turn=await model.Complete([new("user","Read the report")],AiToolRegistry.All,TestContext.Current.CancellationToken);
        var call=Assert.Single(turn.Calls);Assert.Equal(name,call.Name);var validated=AiToolRegistry.Validate(AiToolRegistry.Find(name),call.Arguments);
        Assert.NotNull(validated.StartDate);Assert.NotNull(validated.EndDate);Assert.False(validated.ComparePrevious);
    }
    [Theory]
    [InlineData("get_top_products","{\"limit\":5}","dates")]
    [InlineData("get_sales_summary","{\"startDate\":null,\"endDate\":null}","comparison")]
    [InlineData("get_sales_summary","{\"comparePrevious\":false}","dates")]
    [InlineData("get_sales_summary","{}","dates_and_comparison")]
    public void Omitted_documented_defaults_map_to_the_existing_strict_contract(string name,string arguments,string expected)
    {
        var canonical=OllamaReportingArguments.Canonicalize(name,arguments,AiToolRegistry.All,out var defaults);
        Assert.Equal(expected,defaults);var validated=AiToolRegistry.Validate(AiToolRegistry.Find(name),canonical);
        Assert.False(validated.ComparePrevious);Assert.NotNull(validated.StartDate);Assert.Equal(TimeSpan.FromDays(30),validated.EndDate-validated.StartDate);
        if(name=="get_top_products")Assert.Equal(5,validated.Limit);
    }
    [Theory]
    [InlineData("{\"limit\":\"5\"}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":11}")]
    [InlineData("{\"limit\":5,\"role\":\"Admin\"}")]
    [InlineData("{\"limit\":5,\"limit\":6}")]
    [InlineData("{\"limit\":5,\"startDate\":null}")]
    [InlineData("{\"limit\":5,\"endDate\":null}")]
    [InlineData("{\"limit\":5,\"startDate\":\"invalid\",\"endDate\":null}")]
    [InlineData("{}")]
    public void Invalid_supplied_values_extra_fields_duplicates_partial_dates_and_missing_limits_are_not_repaired(string arguments)
    {
        var canonical=OllamaReportingArguments.Canonicalize("get_top_products",arguments,AiToolRegistry.All,out var defaults);
        Assert.Null(defaults);Assert.Equal(arguments,canonical);Assert.Throws<BusinessException>(()=>AiToolRegistry.Validate(AiToolRegistry.Find("get_top_products"),canonical));
    }
    [Theory]
    [InlineData("delete_users")]
    [InlineData("suggest_workflow_draft")]
    [InlineData("route_erp_request")]
    public void Unregistered_or_non_report_tools_are_never_normalized(string name)
    {Assert.Equal("{}",OllamaReportingArguments.Canonicalize(name,"{}",AiToolRegistry.All,out var defaults));Assert.Null(defaults);}
    [Fact]
    public void A_report_outside_the_offered_inventory_scope_is_not_normalized()
    {Assert.Equal("{\"limit\":5}",OllamaReportingArguments.Canonicalize("get_top_products","{\"limit\":5}",InventoryAgent.Tools,out var defaults));Assert.Null(defaults);}
    [Fact]
    public void Explicit_periods_and_comparison_are_preserved_and_the_registry_stays_strict()
    {
        const string explicitArguments="{\"startDate\":\"2024-02-01T00:00:00Z\",\"endDate\":\"2024-03-01T00:00:00Z\",\"comparePrevious\":true}";
        Assert.Equal(explicitArguments,OllamaReportingArguments.Canonicalize("get_sales_summary",explicitArguments,AiToolRegistry.All,out var defaults));Assert.Null(defaults);
        Assert.True(AiToolRegistry.Validate(AiToolRegistry.Find("get_sales_summary"),explicitArguments).ComparePrevious);
        Assert.Throws<BusinessException>(()=>AiToolRegistry.Validate(AiToolRegistry.Find("get_top_products"),"{\"limit\":5}"));
        Assert.Equal("{\"startDate\":null,\"endDate\":null,\"comparePrevious\":\"false\"}",OllamaReportingArguments.Canonicalize("get_sales_summary","{\"startDate\":null,\"endDate\":null,\"comparePrevious\":\"false\"}",AiToolRegistry.All,out defaults));Assert.Null(defaults);
    }
}
