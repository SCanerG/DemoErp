using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Demo.Api.Tests;
public sealed class BusinessTests(ApiFixture fixture) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    private HttpClient client = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public async ValueTask InitializeAsync()
    {
        client = fixture.App.CreateClient();
        var email = $"business-{Guid.NewGuid():N}@example.com";
        // Auth HTTP behavior is covered separately; create independent fixture accounts without exhausting the limiter.
        using var scope = fixture.App.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        await auth.Register(new("Business", email, "BusinessPassword123!"), Ct);
        var login = await auth.Login(new(email, "BusinessPassword123!"), Ct);
        client.DefaultRequestHeaders.Authorization = new("Bearer", login!.AccessToken);
    }
    public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    private async Task<(Category Category, Customer Customer, Product First, Product Second)> Seed()
    {
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Name = "Business" };
        var customer = new Customer { Name = "Acme", Email = "acme@example.com" };
        var first = new Product { Name = "First", CategoryId = category.Id, Price = 19.95m, IsActive = true };
        var second = new Product { Name = "Second", CategoryId = category.Id, Price = 3.50m, IsActive = true };
        db.AddRange(category, customer, first, second); await db.SaveChangesAsync(Ct);
        return (category, customer, first, second);
    }
    [Theory]
    [InlineData("categories")][InlineData("customers")][InlineData("orders")]
    public async Task New_endpoints_require_authentication(string route)
    {
        using var anonymous = fixture.App.CreateClient();
        var id = Guid.NewGuid();
        foreach (var (method, path) in new[] { ("GET", $"/api/{route}"), ("GET", $"/api/{route}/{id}"), ("POST", $"/api/{route}"), ("PUT", route == "orders" ? $"/api/orders/{id}/status" : $"/api/{route}/{id}") }.Concat(route == "orders" ? [] : new[] { ("DELETE", $"/api/{route}/{id}") }))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new(new HttpMethod(method), path), Ct)).StatusCode);
    }
    [Fact]
    public async Task Product_requires_an_existing_active_category_and_referenced_categories_cannot_be_deleted()
    {
        var data = await Seed();
        var input = new ProductRequest("New", "", 1m, true);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", input, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", input with { CategoryId = Guid.NewGuid() }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/products", input with { CategoryId = data.Category.Id }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/categories/{data.Category.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/categories/{data.Category.Id}", Ct)).StatusCode);
    }
    [Fact]
    public async Task Category_and_customer_crud_and_validation_work()
    {
        var request = new CategoryRequest("Office", "Category", true);
        var response = await client.PostAsJsonAsync("/api/categories", request, Ct); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = (await response.Content.ReadFromJsonAsync<CategoryResponse>(Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/categories/{category.Id}", request with { Name = "Updated" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/categories/{category.Id}", Ct)).StatusCode);
        foreach (var invalid in new[] { new CustomerRequest(" ", "", "", "", true), new CustomerRequest("Test", "bad-email", "", "", true), new CustomerRequest("Test", "", new string('x', 41), "", true) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customers", invalid, Ct)).StatusCode);
        var customerRequest = new CustomerRequest("Customer", "", "123", "Address", true);
        var created = await client.PostAsJsonAsync("/api/customers", customerRequest, Ct); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var customer = (await created.Content.ReadFromJsonAsync<CustomerResponse>(Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/customers/{customer.Id}", customerRequest with { Name = "Updated" }, Ct)).StatusCode);
        Assert.Equal("Updated", (await client.GetFromJsonAsync<CustomerResponse>($"/api/customers/{customer.Id}", Ct))!.Name);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/customers/{customer.Id}", Ct)).StatusCode);
    }
    [Theory]
    [InlineData("customer")][InlineData("missingCustomer")][InlineData("empty")][InlineData("zero")][InlineData("negative")][InlineData("missingProduct")][InlineData("inactive")][InlineData("duplicate")]
    public async Task Invalid_orders_leave_no_partial_data(string invalid)
    {
        var data = await Seed();
        var input = new OrderRequest(data.Customer.Id, [new(data.First.Id, 2)]);
        if (invalid == "customer") input = input with { CustomerId = Guid.Empty };
        if (invalid == "missingCustomer") input = input with { CustomerId = Guid.NewGuid() };
        if (invalid == "empty") input = input with { Items = [] };
        if (invalid == "zero") input = input with { Items = [new(data.First.Id, 0)] };
        if (invalid == "negative") input = input with { Items = [new(data.First.Id, -1)] };
        if (invalid == "missingProduct") input = input with { Items = [new(data.First.Id, 1), new(Guid.NewGuid(), 1)] };
        if (invalid == "duplicate") input = input with { Items = [new(data.First.Id, 1), new(data.First.Id, 1)] };
        if (invalid == "inactive") { using var scope = fixture.App.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.Where(p => p.Id == data.First.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.IsActive, false), Ct); }
        using var beforeScope = fixture.App.Services.CreateScope(); var beforeDb = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = await beforeDb.Orders.CountAsync(Ct); var items = await beforeDb.OrderItems.CountAsync(Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", input, Ct)).StatusCode);
        Assert.Equal(orders, await beforeDb.Orders.CountAsync(Ct)); Assert.Equal(items, await beforeDb.OrderItems.CountAsync(Ct));
    }
    [Fact]
    public async Task Server_prices_multiple_lines_and_snapshots_are_authoritative()
    {
        var data = await Seed();
        var response = await client.PostAsJsonAsync("/api/orders", new { customerId = data.Customer.Id, totalAmount = 0, items = new[] { new { productId = data.First.Id, quantity = 2, unitPrice = 0, lineTotal = 0 }, new { productId = data.Second.Id, quantity = 3, unitPrice = 99999, lineTotal = 0 } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct))!;
        Assert.Equal(50.40m, order.TotalAmount); Assert.Equal(2, order.Items.Count);
        Assert.Equal(39.90m, order.Items.Single(i => i.ProductId == data.First.Id).LineTotal);
        Assert.Equal(19.95m, order.Items.Single(i => i.ProductId == data.First.Id).UnitPrice);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Products.Where(p => p.Id == data.First.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.Price, 99m), Ct);
        Assert.Equal(50.40m, (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}", Json, Ct))!.TotalAmount);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/products/{data.First.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/customers/{data.Customer.Id}", Ct)).StatusCode);
    }
    [Fact]
    public async Task Concurrent_orders_get_unique_numbers_and_status_rules_hold()
    {
        var data = await Seed(); var input = new OrderRequest(data.Customer.Id, [new(data.First.Id, 1)]);
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/orders", input, Ct), client.PostAsJsonAsync("/api/orders", input, Ct));
        foreach (var response in responses) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var first = (await responses[0].Content.ReadFromJsonAsync<OrderResponse>(Json, Ct))!;
        var second = (await responses[1].Content.ReadFromJsonAsync<OrderResponse>(Json, Ct))!;
        Assert.NotEqual(first.OrderNumber, second.OrderNumber);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/orders/{first.Id}/status", new { status = "Completed" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/orders/{first.Id}/status", new { status = "Confirmed" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/orders/{first.Id}/status", new { status = "Completed" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/orders/{first.Id}/status", new { status = "Cancelled" }, Ct)).StatusCode);
    }
    [Fact]
    public async Task Failure_after_insert_rolls_back_order_and_items()
    {
        var data = await Seed();
        using var scope = fixture.App.Services.CreateScope(); var original = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = original.Database.GetConnectionString();
        var beforeOrders = await original.Orders.CountAsync(Ct); var beforeItems = await original.OrderItems.CountAsync(Ct);
        await using var failing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new FailAfterSave()).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrderService(failing).Create(new(data.Customer.Id, [new(data.First.Id, 1)]), Ct));
        Assert.Equal(beforeOrders, await original.Orders.CountAsync(Ct)); Assert.Equal(beforeItems, await original.OrderItems.CountAsync(Ct));
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Test failure after SQL writes, before commit.");
    }
}
