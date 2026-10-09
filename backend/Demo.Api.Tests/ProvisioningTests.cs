using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Demo.Api.Tests;

public sealed class ProvisioningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static Dictionary<string, string?> Settings(string password = "BootstrapTestOnly123!") => new()
    {
        ["Bootstrap:Enabled"] = "true", ["Bootstrap:Name"] = "Bootstrap reviewer", ["Bootstrap:Email"] = "bootstrap-test@example.com", ["Bootstrap:Password"] = password
    };
    [Fact]
    public async Task Bootstrap_is_one_time_restart_safe_and_last_admin_remains_protected()
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("bootstrap_tests").WithUsername("catalog").WithPassword("test-only-password").Build();
        await database.StartAsync(Ct);
        Guid firstId; string firstHash;
        await using (var original = new TestApplication(database.GetConnectionString()))
        await using (var app = original.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Settings()))))
        {
            using var client = app.CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("bootstrap-test@example.com", "BootstrapTestOnly123!"), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json, Ct); Assert.NotNull(login); Assert.Equal(UserRole.Admin, login.User.Role);
            client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
            firstId = login.User.Id;
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/users/{firstId}/role", new { role = "Viewer" }, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/users/{firstId}/status", new { isActive = false }, Ct)).StatusCode);
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var first = await db.Users.SingleAsync(Ct);
            Assert.True(first.IsActive); Assert.True(first.IsBootstrapAccount); Assert.Equal(UserRole.Admin, first.Role); firstHash = first.PasswordHash;
            Assert.DoesNotContain("BootstrapTestOnly123!", string.Join('\n', original.Logs.Messages)); Assert.DoesNotContain(firstHash, string.Join('\n', original.Logs.Messages));
        }
        await using (var original = new TestApplication(database.GetConnectionString()))
        await using (var app = original.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Settings("DifferentTestOnly123!")))))
        {
            using var client = app.CreateClient(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await db.Users.SingleAsync(Ct); Assert.Equal(firstId, first.Id); Assert.Equal(firstHash, first.PasswordHash);
            // Administrative SQL simulates an old provisioned account no longer being Admin.
            await db.Users.Where(u => u.Id == firstId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Viewer).SetProperty(u => u.IsActive, false), Ct);
        }
        await using (var original = new TestApplication(database.GetConnectionString()))
        await using (var app = original.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Settings()))))
        {
            using var client = app.CreateClient(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(Ct)); var first = await db.Users.SingleAsync(Ct); Assert.False(first.IsActive); Assert.Equal(UserRole.Viewer, first.Role);
        }
    }
    [Fact]
    public async Task Security_migration_preserves_users_hashes_inventory_orders_and_backfills_Viewer()
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("security_upgrade").WithUsername("catalog").WithPassword("test-only-password").Build();
        await database.StartAsync(Ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261009114734_InventoryManagement", Ct);
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var legacyHash = new PasswordHasher<User>().HashPassword(new User(), "LegacyTestPassword123!");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\",\"Name\",\"Email\",\"PasswordHash\",\"CreatedAt\") VALUES ({id},'Legacy user','legacy@example.com',{legacyHash},{now})", Ct);
        await migrator.MigrateAsync(cancellationToken: Ct);
        var user = await db.Users.SingleAsync(Ct); Assert.Equal(id, user.Id); Assert.Equal(legacyHash, user.PasswordHash); Assert.Equal(UserRole.Viewer, user.Role); Assert.True(user.IsActive); Assert.Equal(0, user.SecurityVersion); Assert.False(user.IsBootstrapAccount);
        Assert.False(db.Database.HasPendingModelChanges());
        await using (var loginApp = new TestApplication(database.GetConnectionString()))
        {
            using var client = loginApp.CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("legacy@example.com", "LegacyTestPassword123!"), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json, Ct); Assert.NotNull(login); Assert.Equal(UserRole.Viewer, login.User.Role);
        }
        await using var original = new TestApplication(database.GetConnectionString());
        var settings = Settings(); settings["Bootstrap:Email"] = "legacy@example.com";
        await using var app = original.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings)));
        Assert.Throws<InvalidOperationException>(() => app.CreateClient());
        db.ChangeTracker.Clear(); Assert.Equal(UserRole.Viewer, (await db.Users.SingleAsync(Ct)).Role); Assert.Equal(1, await db.Users.CountAsync(Ct));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Concurrent_mutual_changes_with_exactly_two_admins_cannot_remove_the_last_active_admin(bool deactivate)
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("last_admin_tests").WithUsername("catalog").WithPassword("test-only-password").Build();
        await database.StartAsync(Ct);
        await using var original = new TestApplication(database.GetConnectionString());
        await using var app = original.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Settings())));
        using var first = app.CreateClient(); using var second = app.CreateClient();
        var loginA = await (await first.PostAsJsonAsync("/api/auth/login", new LoginRequest("bootstrap-test@example.com", "BootstrapTestOnly123!"), Ct)).Content.ReadFromJsonAsync<LoginResponse>(Json, Ct); Assert.NotNull(loginA);
        first.DefaultRequestHeaders.Authorization = new("Bearer", loginA.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await first.PostAsJsonAsync("/api/users", new { name = "Second Admin", email = "second@example.com", password = "SecondAdminTest123!", role = "Admin" }, Ct)).StatusCode);
        var loginB = await (await second.PostAsJsonAsync("/api/auth/login", new LoginRequest("second@example.com", "SecondAdminTest123!"), Ct)).Content.ReadFromJsonAsync<LoginResponse>(Json, Ct); Assert.NotNull(loginB);
        second.DefaultRequestHeaders.Authorization = new("Bearer", loginB.AccessToken);
        var suffix = deactivate ? "status" : "role"; object body = deactivate ? new { isActive = false } : new { role = "Viewer" };
        var results = await Task.WhenAll(first.PutAsJsonAsync($"/api/users/{loginB.User.Id}/{suffix}", body, Ct), second.PutAsJsonAsync($"/api/users/{loginA.User.Id}/{suffix}", body, Ct));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Unauthorized);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, Ct));
    }
}
