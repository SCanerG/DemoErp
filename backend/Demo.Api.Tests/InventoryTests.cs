using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Demo.Api.Tests;

public sealed class InventoryTests(ApiFixture fixture) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    private HttpClient client = null!;
    private Guid userId;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public async ValueTask InitializeAsync()
    {
        client = fixture.App.CreateClient();
        using var scope = fixture.App.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var email = $"inventory-{Guid.NewGuid():N}@example.com";
        var user = await auth.Register(new("Inventory reviewer", email, "InventoryTestPassword123!"), Ct);
        userId = user!.Id;
        var setupDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await setupDb.Users.SingleAsync(u => u.Email == email, Ct)).Role = UserRole.Admin;
        await setupDb.SaveChangesAsync(Ct);
        var login = await auth.Login(new(email, "InventoryTestPassword123!"), Ct);
        client.DefaultRequestHeaders.Authorization = new("Bearer", login!.AccessToken);
    }
    public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    private async Task<(Product Product, Customer Customer)> Seed(int stock = 0)
    {
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Name = "Inventory test" };
        var customer = new Customer { Name = "Inventory customer" };
        var product = new Product { Name = "Inventory product", CategoryId = category.Id, Price = 2.50m, IsActive = true };
        db.AddRange(category, customer, product); await db.SaveChangesAsync(Ct);
        if (stock > 0) Assert.Equal(HttpStatusCode.OK, (await Stock(product.Id, "stock-in", stock)).StatusCode);
        return (product, customer);
    }
    private Task<HttpResponseMessage> Stock(Guid productId, string operation, int quantity, string reason = "Test movement") =>
        client.PostAsJsonAsync($"/api/inventory/{productId}/{operation}", new StockRequest(quantity, reason), Ct);
    private Task<HttpResponseMessage> Status(Guid orderId, string status) => client.PutAsJsonAsync($"/api/orders/{orderId}/status", new { status }, Ct);
    private async Task<InventoryResponse> Inventory(Guid productId) => (await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{productId}", Ct))!;
    private async Task<List<MovementResponse>> Movements(Guid productId) => (await client.GetFromJsonAsync<List<MovementResponse>>($"/api/inventory/{productId}/movements", Json, Ct))!;
    private async Task<OrderResponse> Order(Guid customer, params (Guid Product, int Quantity)[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new OrderRequest(customer, lines.Select(l => new OrderItemRequest(l.Product, l.Quantity)).ToList()), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct))!;
    }
    private async Task<OrderResponse> ReadOrder(Guid id) => (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{id}", Json, Ct))!;

    [Theory]
    [InlineData("GET", "")][InlineData("GET", "/id")][InlineData("GET", "/id/movements")]
    [InlineData("POST", "/id/stock-in")][InlineData("POST", "/id/stock-out")][InlineData("POST", "/id/adjust")][InlineData("PUT", "/id/minimum-level")]
    public async Task All_inventory_endpoints_require_authentication(string method, string path)
    {
        using var anonymous = fixture.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new(new HttpMethod(method), "/api/inventory" + path.Replace("id", Guid.NewGuid().ToString())), Ct)).StatusCode);
    }
    [Fact]
    public async Task New_product_has_exactly_one_zero_inventory_and_product_payload_cannot_set_stock()
    {
        var seed = await Seed();
        var response = await client.PostAsJsonAsync("/api/products", new { name = "Zero inventory", description = "", price = 1, isActive = true, categoryId = seed.Product.CategoryId, quantityOnHand = 999, minimumStockLevel = 999 }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = (await response.Content.ReadFromJsonAsync<ProductResponse>(Ct))!;
        var inventory = await Inventory(product.Id);
        Assert.Equal(0, inventory.QuantityOnHand); Assert.Equal(0, inventory.MinimumStockLevel);
        Assert.Empty(await Movements(product.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{product.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/inventory/{product.Id}", Ct)).StatusCode);
    }
    [Theory]
    [InlineData("stock-in", null, "StockIn", 15)]
    [InlineData("stock-out", null, "StockOut", 5)]
    [InlineData("adjust", "Increase", "AdjustmentIncrease", 15)]
    [InlineData("adjust", "Decrease", "AdjustmentDecrease", 5)]
    public async Task Operations_change_stock_and_record_authenticated_audit(string operation, string? direction, string type, int after)
    {
        var data = await Seed(10);
        var response = direction is null ? await Stock(data.Product.Id, operation, 5, "  Count reconciliation  ")
            : await client.PostAsJsonAsync($"/api/inventory/{data.Product.Id}/adjust", new { quantity = 5, direction, reason = "  Count reconciliation  " }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(after, (await Inventory(data.Product.Id)).QuantityOnHand);
        var movement = Assert.Single(await Movements(data.Product.Id), m => m.MovementType.ToString() == type && m.Reason == "Count reconciliation");
        Assert.Equal(10, movement.QuantityBefore); Assert.Equal(after, movement.QuantityAfter); Assert.Equal(5, movement.Quantity);
        Assert.Equal(userId, movement.CreatedByUserId); Assert.Equal("Inventory reviewer", movement.PerformedBy);
        Assert.Equal(TimeSpan.Zero, movement.CreatedAt.Offset); Assert.Null(movement.ReferenceId); Assert.Equal("Manual", movement.ReferenceType);
    }
    [Theory]
    [InlineData("stock-in", "{\"quantity\":-1,\"reason\":\"Invalid\"}")]
    [InlineData("stock-out", "{\"quantity\":0,\"reason\":\"Invalid\"}")]
    [InlineData("stock-in", "{\"quantity\":1.5,\"reason\":\"Invalid\"}")]
    [InlineData("stock-in", "{\"quantity\":1,\"reason\":\"  \"}")]
    [InlineData("adjust", "{\"quantity\":1,\"direction\":\"Increase\",\"reason\":\"\"}")]
    [InlineData("adjust", "{\"quantity\":1,\"reason\":\"Missing direction\"}")]
    [InlineData("adjust", "{\"quantity\":1,\"direction\":\"Invalid\",\"reason\":\"Invalid\"}")]
    [InlineData("minimum-level", "{\"minimumStockLevel\":-1}")]
    [InlineData("minimum-level", "{}")]
    public async Task Invalid_operations_make_no_stock_or_audit_changes(string operation, string json)
    {
        var data = await Seed(10);
        var method = operation == "minimum-level" ? HttpMethod.Put : HttpMethod.Post;
        var response = await client.SendAsync(new(method, $"/api/inventory/{data.Product.Id}/{operation}") { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.Single(await Movements(data.Product.Id));
    }
    [Fact]
    public async Task Insufficient_stock_rejects_without_movement_and_missing_inventory_returns_404()
    {
        var data = await Seed(3);
        var response = await Stock(data.Product.Id, "stock-out", 4);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("insufficientStock", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(3, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.Single(await Movements(data.Product.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await Stock(Guid.NewGuid(), "stock-in", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/inventory/{Guid.NewGuid()}/movements", Ct)).StatusCode);
    }
    [Fact]
    public async Task Minimum_level_updates_without_changing_quantity_or_creating_movement()
    {
        var data = await Seed(5);
        var response = await client.PutAsJsonAsync($"/api/inventory/{data.Product.Id}/minimum-level", new MinimumLevelRequest(5), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inventory = await Inventory(data.Product.Id); Assert.Equal(5, inventory.QuantityOnHand); Assert.Equal(5, inventory.MinimumStockLevel);
        Assert.Single(await Movements(data.Product.Id));
    }
    [Fact]
    public async Task Database_enforces_one_inventory_nonnegative_levels_and_immutable_history()
    {
        var data = await Seed(5);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movementId = (await Movements(data.Product.Id)).Single().Id;
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"InventoryMovements\" SET \"Reason\" = 'Changed' WHERE \"Id\" = {movementId}", Ct))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"InventoryMovements\" WHERE \"Id\" = {movementId}", Ct))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Inventories\" SET \"QuantityOnHand\" = -1 WHERE \"ProductId\" = {data.Product.Id}", Ct))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Inventories\" WHERE \"ProductId\" = {data.Product.Id}", Ct))).SqlState);
        var extra = Guid.NewGuid();
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Inventories\" VALUES ({extra}, {data.Product.Id}, 0, 0, CURRENT_TIMESTAMP)", Ct))).SqlState);
        await Stock(data.Product.Id, "stock-out", 5);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/products/{data.Product.Id}", Ct)).StatusCode);
        Assert.Equal(2, (await Movements(data.Product.Id)).Count);
    }
    [Fact]
    public async Task Pending_confirmation_completion_and_repeated_confirmation_have_exactly_one_deduction()
    {
        var data = await Seed(10);
        var order = await Order(data.Customer.Id, (data.Product.Id, 4));
        Assert.Equal(10, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.False(order.InventoryWasDeducted);
        Assert.Equal(HttpStatusCode.OK, (await Status(order.Id, "Confirmed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Status(order.Id, "Confirmed")).StatusCode);
        Assert.Equal(6, (await Inventory(data.Product.Id)).QuantityOnHand);
        Assert.True((await ReadOrder(order.Id)).InventoryWasDeducted);
        Assert.Equal(HttpStatusCode.OK, (await Status(order.Id, "Completed")).StatusCode);
        Assert.Equal(6, (await Inventory(data.Product.Id)).QuantityOnHand);
        var deduction = Assert.Single(await Movements(data.Product.Id), m => m.MovementType == MovementType.OrderDeduction);
        Assert.Equal(order.Id, deduction.ReferenceId); Assert.Equal(order.OrderNumber, deduction.OrderNumber); Assert.Equal(userId, deduction.CreatedByUserId);
        Assert.Equal(HttpStatusCode.Conflict, (await Status(order.Id, "Cancelled")).StatusCode);
    }
    [Fact]
    public async Task Multi_product_shortage_rolls_back_all_stock_and_keeps_order_pending()
    {
        var first = await Seed(10); var second = await Seed();
        var order = await Order(first.Customer.Id, (first.Product.Id, 4), (second.Product.Id, 1));
        Assert.Equal(HttpStatusCode.Conflict, (await Status(order.Id, "Confirmed")).StatusCode);
        Assert.Equal(OrderStatus.Pending, (await ReadOrder(order.Id)).Status);
        Assert.Equal(10, (await Inventory(first.Product.Id)).QuantityOnHand); Assert.Equal(0, (await Inventory(second.Product.Id)).QuantityOnHand);
        Assert.Single(await Movements(first.Product.Id)); Assert.Empty(await Movements(second.Product.Id));
    }
    [Fact]
    public async Task Pending_cancellation_has_no_stock_effect_and_confirmed_cancellation_returns_once()
    {
        var data = await Seed(10);
        var pending = await Order(data.Customer.Id, (data.Product.Id, 3));
        Assert.Equal(HttpStatusCode.OK, (await Status(pending.Id, "Cancelled")).StatusCode);
        Assert.Equal(10, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.Single(await Movements(data.Product.Id));
        var confirmed = await Order(data.Customer.Id, (data.Product.Id, 4));
        await Status(confirmed.Id, "Confirmed");
        Assert.Equal(6, (await Inventory(data.Product.Id)).QuantityOnHand);
        await Status(confirmed.Id, "Cancelled"); await Status(confirmed.Id, "Cancelled");
        Assert.Equal(10, (await Inventory(data.Product.Id)).QuantityOnHand);
        Assert.Single(await Movements(data.Product.Id), m => m.MovementType == MovementType.OrderCancellationReturn);
        Assert.Equal(HttpStatusCode.Conflict, (await Status(confirmed.Id, "Confirmed")).StatusCode);
    }
    [Fact]
    public async Task Duplicate_legacy_order_lines_are_aggregated_into_one_deduction()
    {
        var data = await Seed(10);
        var order = new Order { CustomerId = data.Customer.Id, OrderNumber = $"LEGACY-{Guid.NewGuid():N}", TotalAmount = 12.5m,
            Items = [new() { ProductId = data.Product.Id, Quantity = 2, UnitPrice = 2.5m, LineTotal = 5m }, new() { ProductId = data.Product.Id, Quantity = 3, UnitPrice = 2.5m, LineTotal = 7.5m }] };
        using (var scope = fixture.App.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Orders.Add(order); await db.SaveChangesAsync(Ct); }
        Assert.Equal(HttpStatusCode.OK, (await Status(order.Id, "Confirmed")).StatusCode);
        Assert.Equal(5, (await Inventory(data.Product.Id)).QuantityOnHand);
        Assert.Equal(5, Assert.Single(await Movements(data.Product.Id), m => m.ReferenceId == order.Id).Quantity);
    }
    [Fact]
    public async Task Legacy_confirmed_order_without_deductions_does_not_invent_a_cancellation_return()
    {
        var data = await Seed(); var order = await Order(data.Customer.Id, (data.Product.Id, 4));
        using (var scope = fixture.App.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(set => set.SetProperty(o => o.Status, OrderStatus.Confirmed), Ct);
        Assert.Equal(HttpStatusCode.OK, (await Status(order.Id, "Cancelled")).StatusCode);
        Assert.Equal(0, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.Empty(await Movements(data.Product.Id));
    }
    [Fact]
    public async Task Simultaneous_different_orders_cannot_oversell()
    {
        var data = await Seed(10);
        var first = await Order(data.Customer.Id, (data.Product.Id, 8)); var second = await Order(data.Customer.Id, (data.Product.Id, 7));
        var results = await Task.WhenAll(Status(first.Id, "Confirmed"), Status(second.Id, "Confirmed"));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var remaining = (await Inventory(data.Product.Id)).QuantityOnHand; Assert.True(remaining is 2 or 3);
        Assert.Single(await Movements(data.Product.Id), m => m.MovementType == MovementType.OrderDeduction);
        Assert.Single(await Task.WhenAll(ReadOrder(first.Id), ReadOrder(second.Id)), o => o.Status == OrderStatus.Pending);
    }
    [Fact]
    public async Task Concurrent_same_order_confirmations_and_cancellations_process_stock_once()
    {
        var data = await Seed(10); var order = await Order(data.Customer.Id, (data.Product.Id, 4));
        foreach (var response in await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Status(order.Id, "Confirmed")))) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, (await Inventory(data.Product.Id)).QuantityOnHand);
        foreach (var response in await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Status(order.Id, "Cancelled")))) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, (await Inventory(data.Product.Id)).QuantityOnHand);
        var movements = await Movements(data.Product.Id);
        Assert.Single(movements, m => m.MovementType == MovementType.OrderDeduction); Assert.Single(movements, m => m.MovementType == MovementType.OrderCancellationReturn);
    }
    [Fact]
    public async Task Concurrent_adjustments_do_not_lose_updates_and_ledger_forms_a_complete_chain()
    {
        var data = await Seed();
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.PostAsJsonAsync($"/api/inventory/{data.Product.Id}/adjust", new { direction = "Increase", quantity = 1, reason = "Concurrent count" }, Ct)));
        foreach (var response in responses) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, (await Inventory(data.Product.Id)).QuantityOnHand);
        var ledger = (await Movements(data.Product.Id)).OrderBy(m => m.QuantityBefore).ToArray();
        Assert.Equal(20, ledger.Length);
        for (var i = 0; i < 20; i++) { Assert.Equal(i, ledger[i].QuantityBefore); Assert.Equal(i + 1, ledger[i].QuantityAfter); }
    }
    [Theory]
    [InlineData("stock")][InlineData("confirm")][InlineData("cancel")]
    public async Task Failure_after_SQL_writes_rolls_back_inventory_ledger_and_order_status(string operation)
    {
        var data = await Seed(10); var order = await Order(data.Customer.Id, (data.Product.Id, 4));
        if (operation == "cancel") Assert.Equal(HttpStatusCode.OK, (await Status(order.Id, "Confirmed")).StatusCode);
        using var scope = fixture.App.Services.CreateScope(); var original = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var failing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(original.Database.GetConnectionString()).AddInterceptors(new FailAfterSave()).Options);
        if (operation != "stock") await Assert.ThrowsAsync<InvalidOperationException>(() => new OrderService(failing).ChangeStatus(order.Id, operation == "cancel" ? OrderStatus.Cancelled : OrderStatus.Confirmed, userId, Ct));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => new InventoryService(failing).Change(data.Product.Id, 3, MovementType.StockOut, userId, "Rollback test", Ct));
        Assert.Equal(operation == "cancel" ? 6 : 10, (await Inventory(data.Product.Id)).QuantityOnHand);
        Assert.Equal(operation == "cancel" ? 2 : 1, (await Movements(data.Product.Id)).Count);
        Assert.Equal(operation == "cancel" ? OrderStatus.Confirmed : OrderStatus.Pending, (await ReadOrder(order.Id)).Status);
    }
    [Theory]
    [InlineData(OrderStatus.Pending, "Completed")][InlineData(OrderStatus.Confirmed, "Pending")]
    [InlineData(OrderStatus.Completed, "Pending")][InlineData(OrderStatus.Completed, "Confirmed")][InlineData(OrderStatus.Completed, "Cancelled")]
    [InlineData(OrderStatus.Cancelled, "Pending")][InlineData(OrderStatus.Cancelled, "Confirmed")][InlineData(OrderStatus.Cancelled, "Completed")]
    public async Task All_invalid_transitions_leave_inventory_and_order_unchanged(OrderStatus initial, string target)
    {
        var data = await Seed(10); var order = await Order(data.Customer.Id, (data.Product.Id, 1));
        using (var scope = fixture.App.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(set => set.SetProperty(o => o.Status, initial), Ct);
        var response = await Status(order.Id, target);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("invalidTransition", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(initial, (await ReadOrder(order.Id)).Status); Assert.Equal(10, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.Single(await Movements(data.Product.Id));
    }
    [Fact]
    public async Task Concurrent_multi_product_orders_with_reverse_line_order_use_consistent_lock_order()
    {
        var first = await Seed(20); var second = await Seed(20);
        var a = await Order(first.Customer.Id, (first.Product.Id, 8), (second.Product.Id, 8));
        var b = await Order(first.Customer.Id, (second.Product.Id, 7), (first.Product.Id, 7));
        foreach (var result in await Task.WhenAll(Status(a.Id, "Confirmed"), Status(b.Id, "Confirmed"))) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(5, (await Inventory(first.Product.Id)).QuantityOnHand); Assert.Equal(5, (await Inventory(second.Product.Id)).QuantityOnHand);
        foreach (var result in await Task.WhenAll(Status(a.Id, "Cancelled"), Status(b.Id, "Cancelled"))) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(20, (await Inventory(first.Product.Id)).QuantityOnHand); Assert.Equal(20, (await Inventory(second.Product.Id)).QuantityOnHand);
    }
    [Fact]
    public async Task Concurrent_stock_withdrawals_never_make_quantity_negative()
    {
        var data = await Seed(10);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Stock(data.Product.Id, "stock-out", 7)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(3, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.Equal(2, (await Movements(data.Product.Id)).Count);
    }
    [Fact]
    public async Task Integer_overflow_is_rejected_and_cancellation_remains_atomic()
    {
        var data = await Seed(int.MaxValue);
        Assert.Equal(HttpStatusCode.Conflict, (await Stock(data.Product.Id, "stock-in", 1)).StatusCode);
        Assert.Equal(int.MaxValue, (await Inventory(data.Product.Id)).QuantityOnHand); Assert.Single(await Movements(data.Product.Id));
        var order = await Order(data.Customer.Id, (data.Product.Id, 1)); await Status(order.Id, "Confirmed");
        await Stock(data.Product.Id, "stock-in", 1);
        Assert.Equal(HttpStatusCode.Conflict, (await Status(order.Id, "Cancelled")).StatusCode);
        Assert.Equal(OrderStatus.Confirmed, (await ReadOrder(order.Id)).Status);
        Assert.DoesNotContain(await Movements(data.Product.Id), m => m.MovementType == MovementType.OrderCancellationReturn);
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Failure after SQL, before commit");
    }
}
