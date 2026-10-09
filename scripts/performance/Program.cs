using System.Data.Common;
using System.Text.Json;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? throw new InvalidOperationException("Set ConnectionStrings__Default for an isolated benchmark database.");
var settings = new NpgsqlConnectionStringBuilder(connectionString);
if (settings.Database != "reporting_benchmark" || settings.Host is not ("localhost" or "127.0.0.1"))
    throw new InvalidOperationException("Only a local database named reporting_benchmark is allowed. Never point this tool at application data.");
var capture = new CommandCapture();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).AddInterceptors(capture).Options);
if (args.Contains("--baseline")) await db.GetService<IMigrator>().MigrateAsync("20261009132422_ReportingCompletion");
else await db.Database.MigrateAsync();
if (args.Contains("--seed"))
{
    if (await db.Products.AnyAsync() || await db.Orders.AnyAsync() || await db.Customers.AnyAsync() || await db.Users.AnyAsync()
        || await db.Categories.AnyAsync() || await db.InventoryMovements.AnyAsync() || await db.AuditLogs.AnyAsync())
        throw new InvalidOperationException("Seed requires an empty benchmark database; existing records will never be removed or overwritten.");
    var path = Path.Combine(AppContext.BaseDirectory, "seed.sql");
    if (!File.Exists(path)) path = Path.Combine("scripts", "performance", "seed.sql");
    await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
    await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(path), connection) { CommandTimeout = 120 };
    await command.ExecuteNonQueryAsync();
    Console.WriteLine("Seeded 500 categories, 5000 products/inventories, 2000 customers, 20000 orders, 80000 items, 100000 movements; synthetic disabled actor.");
}
if (!args.Contains("--measure")) return;
var outputIndex = Array.IndexOf(args, "--output");
if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new InvalidOperationException("Specify --output <directory> for plans and timings.");
var output = Path.GetFullPath(args[outputIndex + 1]); Directory.CreateDirectory(output);
var filter = new ReportFilter { Start = new(2025, 9, 1, 0, 0, 0, TimeSpan.Zero), End = new(2025, 10, 1, 0, 0, 0, TimeSpan.Zero), PageSize = 20 };
var reports = new ReportingService(db);
var productId = await db.Products.OrderBy(p => p.Name).Select(p => p.Id).FirstAsync();
var queries = new Dictionary<string, Func<Task>>
{
    ["sales-total"] = async () => { await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Completed && o.CompletedAt >= filter.Start && o.CompletedAt < filter.End)
        .GroupBy(o => 1).Select(g => new { Sales = g.Sum(o => o.TotalAmount), Count = g.Count() }).SingleOrDefaultAsync(); },
    ["top-products"] = async () => { await reports.Products(filter).Take(10).ToListAsync(); },
    ["customer-summary"] = async () => { await reports.Customers(filter).Take(20).ToListAsync(); },
    ["filtered-orders"] = async () => { await reports.Sales(new() { Start = filter.Start, End = filter.End, Status = OrderStatus.Pending }).Take(20).ToListAsync(); },
    ["inventory-joins"] = async () => { await reports.Inventory(new() { CategoryId = await db.Products.Where(p => p.Id == productId).Select(p => p.CategoryId).SingleAsync(), Descending = false }).Take(20).ToListAsync(); },
    ["movement-history"] = async () => { await db.InventoryMovements.AsNoTracking().Where(m => m.ProductId == productId).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
        .Select(m => new { m.Id, m.Quantity, m.QuantityBefore, m.QuantityAfter, Actor = m.CreatedByUser.Name, m.CreatedAt }).ToListAsync(); }
};
await using var planConnection = new NpgsqlConnection(connectionString); await planConnection.OpenAsync();
await using (var analyze = new NpgsqlCommand("ANALYZE", planConnection)) await analyze.ExecuteNonQueryAsync();
var measurements = new List<object>();
foreach (var (name, execute) in queries)
{
    await execute(); // EF compilation + capture, not timed. Capture the actual command and typed parameter values.
    var sql = capture.Sql; var parameters = capture.Parameters;
    await File.WriteAllTextAsync(Path.Combine(output, name + ".sql"), sql);
    var times = new List<double>(); string? plan = null;
    for (var run = 0; run < 8; run++)
    {
        await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql, planConnection);
        foreach (var parameter in parameters) command.Parameters.Add((NpgsqlParameter)parameter.Clone());
        var json = (string)(await command.ExecuteScalarAsync())!;
        using var document = JsonDocument.Parse(json);
        if (run > 0) times.Add(document.RootElement[0].GetProperty("Execution Time").GetDouble());
        plan = json;
    }
    var ordered = times.Order().ToArray(); var median = ordered[ordered.Length / 2];
    await File.WriteAllTextAsync(Path.Combine(output, name + ".plan.json"), plan);
    var safeParameters = parameters.Select(p => new { p.ParameterName, Value = p.Value, Type = p.NpgsqlDbType.ToString() });
    measurements.Add(new { Query = name, MedianMs = median, SamplesMs = times, Parameters = safeParameters });
    Console.WriteLine($"{name}: warm median {median:F3} ms (7 runs)");
}
await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(new {
    MeasuredAt = DateTimeOffset.UtcNow, Runtime = Environment.Version.ToString(), PostgreSql = planConnection.PostgreSqlVersion.ToString(),
    Method = "Actual EF command + typed parameters, EXPLAIN ANALYZE BUFFERS JSON. One discarded warmup, seven warm executions. No cold-cache claim.",
    Rows = new { Categories = await db.Categories.CountAsync(), Products = await db.Products.CountAsync(), Customers = await db.Customers.CountAsync(), Orders = await db.Orders.CountAsync(), OrderItems = await db.OrderItems.CountAsync(), Movements = await db.InventoryMovements.CountAsync() }, Measurements = measurements
}, new JsonSerializerOptions { WriteIndented = true }));

sealed class CommandCapture : DbCommandInterceptor
{
    public string Sql { get; private set; } = "";
    public NpgsqlParameter[] Parameters { get; private set; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Sql = command.CommandText;
        Parameters = command.Parameters.Cast<NpgsqlParameter>().Select(p => (NpgsqlParameter)p.Clone()).ToArray();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
