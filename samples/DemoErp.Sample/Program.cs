using DemoErp.Sample;

if (args.Contains("--self-test"))
{
    if (SizeRule.Evaluate(new(160, 230, "Rectangle")).GrossAreaM2 != 3.68m)
        throw new Exception("Area check failed.");
    foreach (var invalid in new[] {
        new SizeRequest(0, 230, "Rectangle"), new SizeRequest(-1, 230, "Rectangle"),
        new SizeRequest(10001, 230, "Rectangle"), new SizeRequest(100, 120, "Round"),
        new SizeRequest(100, 120, "Square"), new SizeRequest(100, 100, "Unknown") })
    {
        var rejected = false;
        try { SizeRule.Evaluate(invalid); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Invalid input was accepted.");
    }
    Console.WriteLine("PASS: gross area and six validation cases.");
    return;
}

// Isolated demonstration only: no production data, authentication or ERP endpoints.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/samples/size/validate", (SizeRequest request) =>
{
    try { return Results.Ok(SizeRule.Evaluate(request)); }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> {
            ["size"] = [ex.Message]
        });
    }
});
app.Run();
