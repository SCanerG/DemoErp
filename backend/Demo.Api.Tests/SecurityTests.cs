using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Demo.Api.Tests;

public sealed class SecurityTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Password = "SecurityTestPassword123!";
    private sealed record Account(User User, HttpClient Client, string Token);
    private async Task<Account> CreateAccount(UserRole role = UserRole.Admin)
    {
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Name = "Security reviewer", Email = $"security-{Guid.NewGuid():N}@example.com", Role = role };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, Password);
        db.Users.Add(user); await db.SaveChangesAsync(Ct);
        var token = scope.ServiceProvider.GetRequiredService<TokenService>().Create(user).AccessToken;
        var client = fixture.App.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return new(user, client, token);
    }
    private async Task<(Product Product, Customer Customer)> Seed()
    {
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Name = "Security category" }; var product = new Product { Name = "Audit product", CategoryId = category.Id, Price = 10, IsActive = true };
        var customer = new Customer { Name = "Audit customer", Phone = "private-phone", Address = "private-address" };
        db.AddRange(category, product, customer); await db.SaveChangesAsync(Ct); return (product, customer);
    }
    private Task<HttpResponseMessage> ChangeRole(Account actor, Guid target, string role) => actor.Client.PutAsJsonAsync($"/api/users/{target}/role", new { role }, Ct);
    private Task<HttpResponseMessage> ChangeStatus(Account actor, Guid target, bool isActive) => actor.Client.PutAsJsonAsync($"/api/users/{target}/status", new { isActive }, Ct);

    [Theory]
    [InlineData("GET", "/api/users")][InlineData("POST", "/api/users")][InlineData("PUT", "/api/users/00000000-0000-0000-0000-000000000001/role")]
    [InlineData("GET", "/api/audit-logs")][InlineData("GET", "/api/audit-logs/00000000-0000-0000-0000-000000000001")]
    public async Task Administrative_endpoints_require_authentication(string method, string path)
    {
        using var request = new HttpRequestMessage(new(method), path) { Content = JsonContent.Create(new { }) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.SendAsync(request, Ct)).StatusCode);
    }
    [Theory]
    [InlineData("POST", "/api/products")][InlineData("PUT", "/api/products/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/categories")][InlineData("PUT", "/api/categories/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/customers")][InlineData("PUT", "/api/customers/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/orders")][InlineData("PUT", "/api/orders/00000000-0000-0000-0000-000000000001/status")]
    [InlineData("POST", "/api/inventory/00000000-0000-0000-0000-000000000001/stock-in")]
    [InlineData("POST", "/api/inventory/00000000-0000-0000-0000-000000000001/stock-out")]
    [InlineData("POST", "/api/inventory/00000000-0000-0000-0000-000000000001/adjust")]
    [InlineData("PUT", "/api/inventory/00000000-0000-0000-0000-000000000001/minimum-level")]
    public async Task Viewer_cannot_write_business_data(string method, string path)
    {
        var viewer = await CreateAccount(UserRole.Viewer); using var client = viewer.Client;
        using var request = new HttpRequestMessage(new(method), path) { Content = JsonContent.Create(new { }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products", Ct)).StatusCode); // 403 does not revoke authentication.
    }
    [Theory]
    [InlineData(UserRole.Manager)][InlineData(UserRole.Viewer)]
    public async Task Non_admins_cannot_delete_or_access_any_administrative_endpoint(UserRole role)
    {
        var account = await CreateAccount(role); using var client = account.Client;
        foreach (var path in new[] { "products", "categories", "customers" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/{path}/{Guid.NewGuid()}", Ct)).StatusCode);
        foreach (var path in new[] { "/api/users", $"/api/users/{Guid.NewGuid()}", "/api/audit-logs", $"/api/audit-logs/{Guid.NewGuid()}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users", new { }, Ct)).StatusCode);
        foreach (var suffix in new[] { "", "/role", "/status" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/users/{Guid.NewGuid()}{suffix}", new { }, Ct)).StatusCode);
    }
    [Theory]
    [InlineData(UserRole.Viewer)][InlineData(UserRole.Manager)][InlineData(UserRole.Admin)]
    public async Task Every_role_can_read_all_business_modules_and_inventory_history(UserRole role)
    {
        var account = await CreateAccount(role); using var client = account.Client; var data = await Seed();
        foreach (var path in new[] { "products", "categories", "customers", "orders", "inventory", $"inventory/{data.Product.Id}", $"inventory/{data.Product.Id}/movements" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/{path}", Ct)).StatusCode);
    }
    [Fact]
    public async Task Manager_can_create_update_products_and_manage_inventory_and_orders()
    {
        var manager = await CreateAccount(UserRole.Manager); using var client = manager.Client; var data = await Seed();
        var request = new ProductRequest("Managed", "", 12, true, data.Product.CategoryId);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/products", request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/products/{data.Product.Id}", request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/inventory/{data.Product.Id}/stock-in", new { quantity = 10, reason = "Supply" }, Ct)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/orders", new { customerId = data.Customer.Id, items = new[] { new { productId = data.Product.Id, quantity = 2 } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var order = await response.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct); Assert.NotNull(order);
        foreach (var status in new[] { "Confirmed", "Completed" })
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status }, Ct)).StatusCode);
    }
    [Fact]
    public async Task Public_registration_ignores_privileged_overposted_fields()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/auth/register", new { name = "Public viewer", email = $"public-{Guid.NewGuid():N}@example.com", password = Password, role = "Admin", isActive = false, securityVersion = 99, isBootstrapAccount = true }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(Json, Ct); Assert.NotNull(user); Assert.Equal(UserRole.Viewer, user.Role); Assert.True(user.IsActive);
        using var scope = fixture.App.Services.CreateScope(); var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == user.Id, Ct);
        Assert.False(stored.IsBootstrapAccount); Assert.Equal(0, stored.SecurityVersion);
    }
    [Theory]
    [InlineData("Viewer")][InlineData("Manager")][InlineData("Admin")]
    public async Task Admin_can_explicitly_create_accounts_and_read_safe_DTOs(string role)
    {
        var admin = await CreateAccount(); using var client = admin.Client;
        var response = await client.PostAsJsonAsync("/api/users", new { name = "Created user", email = $"new-{Guid.NewGuid():N}@example.com", password = Password, role, securityVersion = 500 }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var user = await response.Content.ReadFromJsonAsync<UserResponse>(Json, Ct); Assert.NotNull(user); Assert.Equal(role, user.Role.ToString());
        foreach (var path in new[] { "/api/users", $"/api/users/{user.Id}", "/api/audit-logs" })
        {
            var read = await client.GetAsync(path, Ct); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var text = await read.Content.ReadAsStringAsync(Ct); Assert.DoesNotContain("passwordHash", text); Assert.DoesNotContain(Password, text); Assert.DoesNotContain(admin.Token, text);
        }
    }
    [Fact]
    public async Task Basic_details_cannot_overpost_role_status_security_version_or_password()
    {
        var admin = await CreateAccount(); using var client = admin.Client; var viewer = await CreateAccount(UserRole.Viewer); using var viewerClient = viewer.Client;
        var response = await client.PutAsJsonAsync($"/api/users/{viewer.User.Id}", new { name = "Renamed", email = viewer.User.Email, role = "Admin", isActive = false, securityVersion = 90, password = "untrusted" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = fixture.App.Services.CreateScope(); var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == viewer.User.Id, Ct);
        Assert.Equal(UserRole.Viewer, stored.Role); Assert.True(stored.IsActive); Assert.Equal(0, stored.SecurityVersion); Assert.Equal(viewer.User.PasswordHash, stored.PasswordHash);
    }
    [Fact]
    public async Task Admin_cannot_change_own_role_or_deactivate_self()
    {
        var admin = await CreateAccount(); using var client = admin.Client;
        Assert.Equal(HttpStatusCode.Conflict, (await ChangeRole(admin, admin.User.Id, "Viewer")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ChangeStatus(admin, admin.User.Id, false)).StatusCode);
    }
    [Fact]
    public async Task Role_change_deactivation_reactivation_and_email_change_revoke_old_tokens()
    {
        var admin = await CreateAccount(); using var client = admin.Client; var target = await CreateAccount(UserRole.Manager); using var targetClient = target.Client;
        Assert.Equal(HttpStatusCode.OK, (await ChangeRole(admin, target.User.Id, "Viewer")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/products", Ct)).StatusCode);
        using var scope = fixture.App.Services.CreateScope(); var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var login = await auth.Login(new(target.User.Email, Password), Ct); Assert.NotNull(login); Assert.Equal(UserRole.Viewer, login.User.Role);
        targetClient.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await ChangeStatus(admin, target.User.Id, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/products", Ct)).StatusCode);
        using (var freshScope = fixture.App.Services.CreateScope())
            Assert.Null(await freshScope.ServiceProvider.GetRequiredService<AuthService>().Login(new(target.User.Email, Password), Ct));
        Assert.Equal(HttpStatusCode.OK, (await ChangeStatus(admin, target.User.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/products", Ct)).StatusCode);
        using var newScope = fixture.App.Services.CreateScope(); var newLogin = await newScope.ServiceProvider.GetRequiredService<AuthService>().Login(new(target.User.Email, Password), Ct); Assert.NotNull(newLogin);
        targetClient.DefaultRequestHeaders.Authorization = new("Bearer", newLogin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{target.User.Id}", new { name = "New email", email = $"changed-{Guid.NewGuid():N}@example.com" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/products", Ct)).StatusCode);
    }
    [Fact]
    public async Task Unique_email_normalization_and_invalid_roles_are_enforced()
    {
        var admin = await CreateAccount(); using var client = admin.Client;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/users", new { name = "Duplicate", email = admin.User.Email.ToUpperInvariant(), password = Password, role = "Viewer" }, Ct)).StatusCode);
        var viewer = await CreateAccount(UserRole.Viewer); using var viewerClient = viewer.Client;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/users/{viewer.User.Id}", new { name = "Duplicate", email = admin.User.Email.ToUpperInvariant() }, Ct)).StatusCode);
        foreach (var role in new object[] { "SuperAdmin", 2, "2" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/users/{viewer.User.Id}/role", new { role }, Ct)).StatusCode);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Concurrent_admin_changes_recheck_actor_after_lock_and_preserve_an_admin(bool deactivate)
    {
        var first = await CreateAccount(); using var a = first.Client; var second = await CreateAccount(); using var b = second.Client;
        var responses = await Task.WhenAll(deactivate ? ChangeStatus(first, second.User.Id, false) : ChangeRole(first, second.User.Id, "Viewer"),
            deactivate ? ChangeStatus(second, first.User.Id, false) : ChangeRole(second, first.User.Id, "Viewer"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => (u.Id == first.User.Id || u.Id == second.User.Id) && u.IsActive && u.Role == UserRole.Admin, Ct));
    }
    [Fact]
    public async Task Product_changes_capture_correct_actor_fields_and_immutable_audit_without_secrets()
    {
        var admin = await CreateAccount(); using var client = admin.Client; var data = await Seed();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/products/{data.Product.Id}", new ProductRequest("Renamed product", "do-not-copy-description", 12, true, data.Product.CategoryId), Ct)).StatusCode);
        var logs = await client.GetFromJsonAsync<AuditPage>($"/api/audit-logs?userId={admin.User.Id}&action=Update&entityName=Product", Json, Ct); Assert.NotNull(logs);
        var log = Assert.Single(logs.Items, a => a.EntityId == data.Product.Id);
        Assert.Equal(admin.User.Name, log.UserName); Assert.Equal(admin.User.Id, log.UserId); Assert.NotNull(log.CorrelationId);
        Assert.Equal(10, log.OldValues!.Value.GetProperty("price").GetDecimal()); Assert.Equal(12, log.NewValues!.Value.GetProperty("price").GetDecimal());
        Assert.False(log.NewValues.Value.TryGetProperty("isActive", out _));
        var detail = await client.GetAsync($"/api/audit-logs/{log.Id}", Ct); Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var text = await detail.Content.ReadAsStringAsync(Ct); Assert.DoesNotContain("do-not-copy-description", text); Assert.DoesNotContain(Password, text); Assert.DoesNotContain(admin.Token, text);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{data.Product.Id}", Ct)).StatusCode);
        var deletions = await client.GetFromJsonAsync<AuditPage>($"/api/audit-logs?userId={admin.User.Id}&action=Delete&entityName=Product", Json, Ct); Assert.NotNull(deletions);
        Assert.Equal(12m, Assert.Single(deletions.Items, a => a.EntityId == data.Product.Id).OldValues!.Value.GetProperty("price").GetDecimal());
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AuditLogs\" WHERE \"Id\"={log.Id}", Ct));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AuditLogs\" SET \"Action\"='Delete' WHERE \"Id\"={log.Id}", Ct));
        Assert.DoesNotContain(Password, string.Join('\n', fixture.App.Logs.Messages)); Assert.DoesNotContain(admin.Token, string.Join('\n', fixture.App.Logs.Messages));
    }
    [Fact]
    public async Task Role_inventory_order_and_minimum_changes_have_audit_entries_and_failed_orders_do_not()
    {
        var admin = await CreateAccount(); using var client = admin.Client; var viewer = await CreateAccount(UserRole.Viewer); using var vc = viewer.Client; var data = await Seed();
        await ChangeRole(admin, viewer.User.Id, "Manager");
        await client.PostAsJsonAsync($"/api/inventory/{data.Product.Id}/stock-in", new { quantity = 10, reason = "Private manual reason remains in movement" }, Ct);
        await client.PostAsJsonAsync($"/api/inventory/{data.Product.Id}/adjust", new { quantity = 2, direction = "Decrease", reason = "Recount" }, Ct);
        await client.PutAsJsonAsync($"/api/inventory/{data.Product.Id}/minimum-level", new { minimumStockLevel = 5 }, Ct);
        var created = await client.PostAsJsonAsync("/api/orders", new { customerId = data.Customer.Id, items = new[] { new { productId = data.Product.Id, quantity = 9 } } }, Ct);
        var order = await created.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct); Assert.NotNull(order);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Confirmed" }, Ct)).StatusCode);
        var before = await client.GetFromJsonAsync<AuditPage>($"/api/audit-logs?userId={admin.User.Id}&action=OrderConfirm", Json, Ct); Assert.NotNull(before); Assert.DoesNotContain(before.Items, x => x.EntityId == order.Id);
        await client.PostAsJsonAsync($"/api/inventory/{data.Product.Id}/stock-in", new { quantity = 1, reason = "Supply" }, Ct);
        await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Confirmed" }, Ct);
        await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Confirmed" }, Ct);
        var logs = await client.GetFromJsonAsync<AuditPage>($"/api/audit-logs?userId={admin.User.Id}&pageSize=100", Json, Ct); Assert.NotNull(logs);
        foreach (var action in new[] { "RoleChange", "StockIn", "StockAdjustment", "MinimumLevelChange", "OrderConfirm" }) Assert.Contains(logs.Items, log => log.Action == action);
        Assert.Single(logs.Items, log => log.Action == "OrderConfirm" && log.EntityId == order.Id);
        var text = JsonSerializer.Serialize(logs); Assert.DoesNotContain("Private manual reason", text); Assert.DoesNotContain("private-phone", text); Assert.DoesNotContain("private-address", text);
    }
    [Fact]
    public async Task Audit_filters_pagination_date_range_and_stable_order_work()
    {
        var admin = await CreateAccount(); using var client = admin.Client;
        for (var i = 0; i < 3; i++) await client.PostAsJsonAsync("/api/categories", new { name = $"Paged {i}", description = "", isActive = true }, Ct);
        var filter = $"/api/audit-logs?userId={admin.User.Id}&action=Create&entityName=Category&pageSize=2&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(2).ToString("O"))}";
        var first = await client.GetFromJsonAsync<AuditPage>(filter, Json, Ct); var second = await client.GetFromJsonAsync<AuditPage>(filter + "&page=2", Json, Ct);
        Assert.NotNull(first); Assert.NotNull(second); Assert.Equal(3, first.TotalCount); Assert.Equal(2, first.Items.Count); Assert.Single(second.Items);
        Assert.DoesNotContain(second.Items[0].Id, first.Items.Select(x => x.Id)); Assert.True(first.Items[0].CreatedAt >= first.Items[1].CreatedAt);
        foreach (var query in new[] { "page=0", "pageSize=101", "action=Unknown", "entityName=Unknown", "from=2030-01-01&to=2020-01-01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/audit-logs?{query}", Ct)).StatusCode);
    }
    [Theory]
    [InlineData("product")][InlineData("role")][InlineData("stock")][InlineData("order")]
    public async Task Audit_insert_failure_rolls_back_critical_business_changes(string operation)
    {
        var admin = await CreateAccount(); using var client = admin.Client; var target = await CreateAccount(UserRole.Viewer); using var tc = target.Client; var data = await Seed();
        await client.PostAsJsonAsync($"/api/inventory/{data.Product.Id}/stock-in", new { quantity = 10, reason = "Initial supply" }, Ct);
        var created = await client.PostAsJsonAsync("/api/orders", new { customerId = data.Customer.Id, items = new[] { new { productId = data.Product.Id, quantity = 2 } } }, Ct);
        var order = await created.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct); Assert.NotNull(order);
        using var scope = fixture.App.Services.CreateScope(); var original = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await original.AuditLogs.CountAsync(Ct);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new("sub", admin.User.Id.ToString()), new("sv", "0"), new("name", admin.User.Name)], "test")) } };
        await using var failing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(original.Database.GetConnectionString()).AddInterceptors(new RejectAuditInsert()).Options, accessor);
        Task Execute() => operation switch
        {
            "product" => new ProductService(failing).Update(data.Product.Id, new("Failed", "", 99, true, data.Product.CategoryId), Ct),
            "role" => new UserService(failing, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(), accessor).Update(target.User.Id, null, UserRole.Manager, null, Ct),
            "stock" => new InventoryService(failing).Change(data.Product.Id, 3, MovementType.StockOut, admin.User.Id, "Failed", Ct),
            _ => new OrderService(failing).ChangeStatus(order.Id, OrderStatus.Confirmed, admin.User.Id, Ct)
        };
        await Assert.ThrowsAsync<DbUpdateException>(Execute);
        Assert.Equal(before, await original.AuditLogs.CountAsync(Ct));
        Assert.Equal(10m, (await original.Products.AsNoTracking().SingleAsync(p => p.Id == data.Product.Id, Ct)).Price);
        Assert.Equal(UserRole.Viewer, (await original.Users.AsNoTracking().SingleAsync(u => u.Id == target.User.Id, Ct)).Role);
        Assert.Equal(10, (await original.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == data.Product.Id, Ct)).QuantityOnHand);
        Assert.Equal(OrderStatus.Pending, (await original.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, Ct)).Status);
    }
    private sealed class RejectAuditInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            // Force a real PostgreSQL FK failure during the INSERT, not a pre-SQL exception.
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added))
                entry.Entity.UserId = Guid.NewGuid();
            return ValueTask.FromResult(result);
        }
    }
}
