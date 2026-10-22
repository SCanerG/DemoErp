using System.Net;
using System.Net.Http.Json;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Demo.Api.Data;
namespace Demo.Api.Tests;
public sealed class AiIntentTests
{
    [Fact]
    public void Order_routing_schema_never_offers_a_sales_or_inventory_workflow()
    {
        using var doc=System.Text.Json.JsonDocument.Parse(AgentPlanner.OrderRouteTool.Schema);
        Assert.Equal(new[]{"order","clarification"},doc.RootElement.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(v=>v.GetString()));
        Assert.Equal(AgentPlanner.RouteTool.Name,AgentPlanner.OrderRouteTool.Name);
    }
    private const string Facts="""{"sales":3812,"products":[{"product":"[DEMO] Sıcaklık Sensörü","currentStock":0},{"product":"[DEMO] Kontrol Kartı","currentStock":18}]}""";
    [Theory]
    [InlineData("B Müşterisi için A Ürününden 10 adet sipariş taslağı hazırla.")]
    [InlineData("Sipariş taslağını hazırlar mısın?")]
    [InlineData("Siparis taslagi hazirla")]
    public void Turkish_inflected_draft_intent_is_order(string question)=>Assert.Equal("order",AgentRouting.Simple(question));
    [Theory]
    [InlineData("Satışlar 3812 TL'dir.","currency_mismatch")]
    [InlineData("Satışlar 3812 EUR.","currency_mismatch")]
    [InlineData("Stokta 18 adet bulunan 'Sıcaklık Sensörü' 0 adet stoka kalmıştır.","stock_claim_mismatch")]
    [InlineData("Kontrol Kartı stoku 0 adet.","stock_claim_mismatch")]
    public void Source_values_cannot_be_reassigned_to_wrong_currency_or_product(string answer,string failure)
    {
        var ex=Assert.Throws<AiProviderException>(()=>AiSummarySemantics.Validate(answer,Facts,"tr"));Assert.Equal(failure,ex.ValidationFailure);
    }
    [Fact]
    public void Correct_product_stock_and_USD_currency_are_accepted()=>AiSummarySemantics.Validate("Satışlar 3.812,00 USD. Sıcaklık Sensörü stoku 0 adet. Kontrol Kartı stoku 18 adet.",Facts,"tr");
}
public sealed partial class AiTests
{
    [Theory]
    [InlineData("sales")]
    [InlineData("inventory")]
    [InlineData("collaboration")]
    public async Task Draft_question_rejects_conflicting_selected_mode_before_any_report(string mode)
    {
        var fake=new FakeModel();using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);
        var id=await StartAgent(client,mode);
        await Code(await ExecuteAgent(client,id,"B Müşterisi için A Ürününden 10 adet sipariş taslağı hazırla.","tr"),HttpStatusCode.BadRequest,"aiAgentModeMismatch");
        var audit=(await AgentStatus(client,id))!;Assert.Empty(audit.Execution.Tools);Assert.Equal(0,fake.Requests);
    }
    [Fact]
    public async Task Auto_draft_question_cannot_be_changed_to_a_sales_plan_by_model()
    {
        var fake=new FakeModel();fake.Turns.Enqueue(new(null,null,[new("route","route_erp_request","""{"kind":"sales","period":"last3months","customer":null,"lines":[]}""")]));
        using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);var id=await StartAgent(client);
        await Code(await ExecuteAgent(client,id,"B Müşterisi için A Ürününden 10 adet sipariş taslağı hazırla.","tr"),HttpStatusCode.BadGateway,"aiInvalidResponse");
        Assert.Empty((await AgentStatus(client,id))!.Execution.Tools);Assert.Equal(1,fake.Requests);
    }
    [Fact]
    public async Task Auto_draft_question_returns_draft_or_clarification_never_sales_and_never_writes_orders()
    {
        var fake=new FakeModel();fake.Turns.Enqueue(RouteOrder("B","A"));fake.Turns.Enqueue(Final("tr"));
        using var app=App(fake);var owner=await Session();using var client=Authorized(app,owner.Token);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var before=await db.Orders.CountAsync(Ct);
        var id=await StartAgent(client);var response=await ExecuteAgent(client,id,"B Müşterisi için A Ürününden 10 adet sipariş taslağı hazırla.","tr");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=(await response.Content.ReadFromJsonAsync<AgentResult>(Ct))!;Assert.Equal("order",result.Intent);Assert.Null(result.Sales);Assert.Null(result.Inventory);Assert.NotNull(result.OrderDraft);Assert.False(result.OrderDraft.Persisted);
        Assert.Equal(before,await db.Orders.CountAsync(Ct));Assert.DoesNotContain(result.Sources,s=>s.Name=="get_sales_summary");
    }
}
