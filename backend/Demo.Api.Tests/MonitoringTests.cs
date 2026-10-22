using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Demo.Api.AI;
using Demo.Api.AI.Agents;
using Demo.Api.AI.Knowledge;
using Demo.Api.AI.Monitoring;
using Demo.Api.Controllers;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Errors;
using Demo.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Demo.Api.Tests;

public sealed class MonitoringTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid InventoryRule = Guid.Parse("11600000-0000-0000-0000-000000000001");
    private static readonly Guid OrdersRule = Guid.Parse("11600000-0000-0000-0000-000000000002");
    public sealed class Analysis : IMonitoringAnalysis
    {
        public List<MonitoringFinding> Findings = [new("test:stock", "outOfStock", "critical", "product", Guid.NewGuid(), "Synthetic part", new() { ["stock"] = 0, ["minimum"] = 3 })];
        public bool Complete = true; public int Failures; public bool Transient; public int Calls;
        public TaskCompletionSource? Entered; public TaskCompletionSource? Continue;
        public async Task<MonitoringAnalysis> Analyze(string kind, MonitoringSettings settings, MonitoringScope scope, DateTimeOffset now, CancellationToken ct)
        {
            scope.Demand(); Calls++; if (Failures-- > 0) { if (Transient) throw new TimeoutException(); throw new BusinessException("monitoringExecutionFailed", 503); }
            Entered?.TrySetResult(); if (Continue is not null) await Continue.Task.WaitAsync(ct);
            return new(Findings, Complete, []);
        }
    }
    private sealed class Model : IAiModelClient
    {
        public Task<AiModelTurn> Complete(IReadOnlyList<AiModelMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken ct)
            => Task.FromResult(new AiModelTurn("Review verified inventory and historical sales before replenishment.", "en", [], 7));
    }
    private WebApplicationFactory<Program> App(Analysis? analysis = null, bool ai = false, IAiModelClient? liveModel = null, AiAssistantOptions? liveSettings = null) => fixture.App.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
        s.RemoveAll<MonitoringOptions>(); s.AddSingleton(new MonitoringOptions { Enabled = true });
        foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MonitoringWorker)).ToList()) s.Remove(d);
        if (analysis is not null) { s.RemoveAll<IMonitoringAnalysis>(); s.AddSingleton<IMonitoringAnalysis>(analysis); }
        s.RemoveAll<AiAssistantOptions>(); s.AddSingleton(liveSettings ?? new AiAssistantOptions { Enabled = ai, Provider = "Mock", AllowDevelopmentMock = true, Model = "deterministic-test" });
        s.RemoveAll<IAiModelClient>(); s.AddSingleton<IAiModelClient>(liveModel ?? new Model());
    }));
    private static async Task<(HttpClient Client, ClaimsPrincipal Principal, Guid Id)> Account(WebApplicationFactory<Program> app, UserRole role = UserRole.Admin)
    {
        using var scope = app.Services.CreateScope(); var auth = scope.ServiceProvider.GetRequiredService<AuthService>(); var email = Guid.NewGuid() + "@example.com";
        await auth.Register(new("Monitor test", email, "MonitorTestPassword123!"), Ct);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var user = await db.Users.SingleAsync(u => u.Email == email, Ct); user.Role = role; await db.SaveChangesAsync(Ct);
        var token = (await auth.Login(new(email, "MonitorTestPassword123!"), Ct))!.AccessToken;
        var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.Id.ToString()), new Claim("sv", user.SecurityVersion.ToString()), new Claim("role", role.ToString()) }, "test", "sub", "role"));
        return (client, principal, user.Id);
    }
    private static async Task Reset(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<MonitoringReview>().ExecuteDeleteAsync(Ct); await db.Set<AlertHistory>().ExecuteDeleteAsync(Ct); await db.Set<OperationalAlert>().ExecuteDeleteAsync(Ct); await db.Set<MonitoringRun>().ExecuteDeleteAsync(Ct);
        foreach (var rule in await db.Set<MonitoringRule>().ToListAsync(Ct)) { rule.Enabled = false; rule.Settings = JsonSerializer.Serialize(new MonitoringSettings(), AiAssistantService.Json); rule.NextRunAt = DateTimeOffset.UtcNow.AddHours(1); }
        await db.SaveChangesAsync(Ct);
    }
    private static async Task<Guid> Queue(WebApplicationFactory<Program> app, ClaimsPrincipal user, Guid? key = null)
    {
        using var scope = app.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<MonitoringEngine>().Enqueue(InventoryRule, key ?? Guid.NewGuid(), user, Ct);
    }
    private static async Task Process(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<MonitoringEngine>().ProcessNext(Ct));
    }
    private static async Task<List<OperationalAlert>> Alerts(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OperationalAlert>().AsNoTracking().ToListAsync(Ct);
    }

    [Fact]
    public async Task Deterministic_monitoring_persists_and_deduplicates_alerts_without_AI_or_orders()
    {
        await using var app = App(new()); var user = await Account(app); await Reset(app);
        var key = Guid.NewGuid(); var first = await Queue(app, user.Principal, key); Assert.Equal(first, await Queue(app, user.Principal, key)); await Process(app);
        await Queue(app, user.Principal); await Process(app); var row = Assert.Single(await Alerts(app));
        Assert.Equal("open", row.Status); Assert.Equal(1, row.Occurrence);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Set<AlertHistory>().Where(h => h.AlertId == row.Id).ToListAsync(Ct));
        Assert.Equal(2, await db.Set<MonitoringRun>().CountAsync(r => r.State == "success", Ct));
        Assert.Equal(0, await db.Set<MonitoringReview>().CountAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/operations/overview", Ct)).StatusCode);
    }
    [Fact]
    public async Task Acknowledge_resolve_dismiss_suppress_repeats_until_condition_clears_and_reopens()
    {
        var analysis = new Analysis(); await using var app = App(analysis); var user = await Account(app); await Reset(app);
        await Queue(app, user.Principal); await Process(app); var alert = Assert.Single(await Alerts(app));
        foreach (var action in new[] { "acknowledged", "resolved", "dismissed" }) Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PostAsJsonAsync($"/api/operations/alerts/{alert.Id}/state", new { action }, Ct)).StatusCode);
        await Queue(app, user.Principal); await Process(app); Assert.Equal("dismissed", Assert.Single(await Alerts(app)).Status);
        analysis.Findings = []; await Queue(app, user.Principal); await Process(app); Assert.False(Assert.Single(await Alerts(app)).ConditionActive);
        analysis.Findings = [new("test:stock", "outOfStock", "critical", "product", null, "Synthetic", new() { ["stock"] = 0 })];
        await Queue(app, user.Principal); await Process(app); var reopened = Assert.Single(await Alerts(app)); Assert.Equal("open", reopened.Status); Assert.Equal(2, reopened.Occurrence);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync($"/api/operations/alerts/{alert.Id}/history", Ct)).StatusCode);
    }
    [Fact]
    public async Task Partial_scan_does_not_clear_previous_findings_and_warning_preferences_do_not_drop_critical_alerts()
    {
        var analysis = new Analysis(); await using var app = App(analysis); var user = await Account(app); await Reset(app);
        await Queue(app, user.Principal); await Process(app); analysis.Findings = []; analysis.Complete = false;
        await Queue(app, user.Principal); await Process(app); Assert.True(Assert.Single(await Alerts(app)).ConditionActive);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var rule = await db.Set<MonitoringRule>().SingleAsync(r => r.Id == InventoryRule, Ct);
        rule.Settings = JsonSerializer.Serialize(new MonitoringSettings(WarningAlerts: false), AiAssistantService.Json); await db.SaveChangesAsync(Ct);
        analysis.Findings = [new("warning:test", "criticalInventory", "warning", "product", null, "", new()), new("critical:test", "outOfStock", "critical", "product", null, "", new())];
        await Queue(app, user.Principal); await Process(app); var all = await Alerts(app); Assert.DoesNotContain(all, a => a.Key == "warning:test"); Assert.Contains(all, a => a.Key == "critical:test");
    }
    [Fact]
    public async Task Two_instances_and_concurrent_enqueues_allow_one_active_job_and_one_claim()
    {
        var analysis = new Analysis(); await using var app = App(analysis); var user = await Account(app); await Reset(app);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => { try { return await Queue(app, user.Principal); } catch (BusinessException e) { Assert.Equal("monitoringBusy", e.Code); return Guid.Empty; } }));
        Assert.Single(results, x => x != Guid.Empty);
        await using var other = App(analysis);
        async Task<bool> Work(WebApplicationFactory<Program> a) { using var scope = a.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<MonitoringEngine>().ProcessNext(Ct); }
        var claims = await Task.WhenAll(Work(app), Work(other)); Assert.Single(claims, x => x); Assert.Equal(1, analysis.Calls); Assert.Single(await Alerts(app));
    }
    [Fact]
    public async Task Cancellation_of_running_job_is_persisted_and_prevents_publication()
    {
        var analysis = new Analysis { Entered = new(), Continue = new() }; await using var app = App(analysis); var user = await Account(app); await Reset(app);
        var id = await Queue(app, user.Principal); var work = Process(app); await analysis.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PostAsync($"/api/operations/runs/{id}/cancel", null, Ct)).StatusCode);
        analysis.Continue.SetResult(); await work; Assert.Empty(await Alerts(app));
        using var scope = app.Services.CreateScope(); Assert.Equal("cancelled", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<MonitoringRun>().SingleAsync(r => r.Id == id, Ct)).State);
    }
    [Fact]
    public async Task Transient_failure_retries_with_backoff_and_does_not_duplicate_alerts()
    {
        var analysis = new Analysis { Failures = 1, Transient = true }; await using var app = App(analysis); var user = await Account(app); await Reset(app);
        var id = await Queue(app, user.Principal); await Process(app);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var row = await db.Set<MonitoringRun>().SingleAsync(r => r.Id == id, Ct); Assert.Equal("retry", row.State); Assert.True(row.AvailableAt > DateTimeOffset.UtcNow); row.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(Ct); }
        await Process(app); Assert.Single(await Alerts(app));
    }
    [Fact]
    public async Task Permanent_failure_emits_single_execution_alert_and_interrupted_job_recovers()
    {
        var analysis = new Analysis { Failures = 1 }; await using var app = App(analysis); var user = await Account(app); await Reset(app);
        await Queue(app, user.Principal); await Process(app); Assert.Equal("agentExecutionFailure", Assert.Single(await Alerts(app)).Category);
        var id = await Queue(app, user.Principal);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var run = await db.Set<MonitoringRun>().SingleAsync(r => r.Id == id, Ct); run.State = "running"; run.Lease = "abandoned"; run.Attempts = 1; run.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3); await db.SaveChangesAsync(Ct); }
        await Process(app); Assert.Contains(await Alerts(app), a => a.Key == "test:stock"); Assert.False((await Alerts(app)).Single(a => a.Category == "agentExecutionFailure").ConditionActive);
    }
    [Fact]
    public async Task Viewer_and_revoked_sessions_cannot_trigger_configure_or_change_alerts()
    {
        await using var app = App(new()); var admin = await Account(app); var viewer = await Account(app, UserRole.Viewer); await Reset(app);
        await Queue(app, admin.Principal); await Process(app); var alert = Assert.Single(await Alerts(app));
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync($"/api/operations/rules/{InventoryRule}/run", new { idempotencyKey = Guid.NewGuid() }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync($"/api/operations/alerts/{alert.Id}/state", new { action = "resolved" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PutAsJsonAsync($"/api/operations/rules/{InventoryRule}", new MonitoringConfiguration(1, new()), Ct)).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var u = await db.Users.SingleAsync(u => u.Id == viewer.Id, Ct); u.IsActive = false; u.SecurityVersion++; await db.SaveChangesAsync(Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.Client.GetAsync("/api/operations/alerts", Ct)).StatusCode);
    }
    [Fact]
    public async Task Private_approval_alerts_and_history_do_not_leak_to_other_users_including_Admin()
    {
        var analysis = new Analysis(); await using var app = App(analysis); var a = await Account(app); var b = await Account(app); await Reset(app);
        analysis.Findings = [new("approval:secret", "pendingApproval", "warning", "approval", Guid.NewGuid(), "", new(), OwnerId: b.Id)];
        await Queue(app, a.Principal); await Process(app); var alert = Assert.Single(await Alerts(app));
        var body = await a.Client.GetStringAsync("/api/operations/alerts?state=all", Ct); Assert.DoesNotContain(alert.Id.ToString(), body);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync($"/api/operations/alerts/{alert.Id}/history", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.PostAsJsonAsync($"/api/operations/alerts/{alert.Id}/state", new { action = "resolved" }, Ct)).StatusCode);
        Assert.Contains(alert.Id.ToString(), await b.Client.GetStringAsync("/api/operations/alerts", Ct));
    }
    [Fact]
    public async Task Rule_configuration_validates_versions_and_roles_and_scheduler_uses_service_identity()
    {
        await using var app = App(new()); var admin = await Account(app); var manager = await Account(app, UserRole.Manager); await Reset(app);
        var rules = await admin.Client.GetFromJsonAsync<JsonElement>("/api/operations/rules", Ct); var version = rules.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == InventoryRule).GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Client.PutAsJsonAsync($"/api/operations/rules/{InventoryRule}", new MonitoringConfiguration(version, new()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PutAsJsonAsync($"/api/operations/rules/{InventoryRule}", new MonitoringConfiguration(version, new(IntervalMinutes: 0)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PutAsJsonAsync($"/api/operations/rules/{InventoryRule}", new MonitoringConfiguration(version, new(Enabled: true)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PutAsJsonAsync($"/api/operations/rules/{InventoryRule}", new MonitoringConfiguration(version, new()), Ct)).StatusCode);
        using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<MonitoringEngine>().Schedule(Ct);
        var run = Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<MonitoringRun>().AsNoTracking().ToListAsync(Ct)); Assert.Null(run.RequestedBy); Assert.Equal(MonitoringScope.Identity, run.Identity);
    }
    [Fact]
    public async Task Event_trigger_observes_existing_business_audit_without_an_event_bus_or_user_impersonation()
    {
        await using var app = App(new()); _ = await Account(app); await Reset(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var rule = await db.Set<MonitoringRule>().SingleAsync(r => r.Id == InventoryRule, Ct);
        rule.Enabled = true; rule.Settings = JsonSerializer.Serialize(new MonitoringSettings(Enabled: true, EventsEnabled: true), AiAssistantService.Json); rule.LastEventAt = DateTimeOffset.UtcNow.AddMinutes(-1); rule.NextEventAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        db.Add(new AuditLog { EntityName = "WarehouseInventory", Action = "StockOut", EntityId = Guid.NewGuid(), Description = "Synthetic event" }); await db.SaveChangesAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<MonitoringEngine>().Schedule(Ct); var run = Assert.Single(await db.Set<MonitoringRun>().AsNoTracking().ToListAsync(Ct)); Assert.Equal("event", run.Trigger);
    }
    [Fact]
    public async Task Actual_inventory_and_orders_use_existing_thresholds_and_real_purchase_dates()
    {
        await using var app = App(); var user = await Account(app); await Reset(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = DateTimeOffset.UtcNow;
        var product = new Product { Name = "Synthetic zero-stock component", Category = new Category { Name = "Monitor category" }, IsActive = true, Price = 10 };
        db.Add(product); await db.SaveChangesAsync(Ct); var warehouse = await db.Warehouses.FirstAsync(Ct); var balance = await db.WarehouseInventories.SingleAsync(i => i.ProductId == product.Id && i.WarehouseId == warehouse.Id, Ct); balance.MinimumStockLevel = 4;
        var supplier = new Supplier { Name = "Monitor supplier", SupplierCode = Guid.NewGuid().ToString("N").ToUpperInvariant(), IsActive = true }; db.Add(supplier);
        var overdue = new PurchaseOrder { PurchaseOrderNumber = Guid.NewGuid().ToString("N"), SupplierId = supplier.Id, CreatedByUserId = user.Id, Status = PurchaseOrderStatus.Submitted, ExpectedDeliveryDate = now.AddDays(-2) };
        var noDate = new PurchaseOrder { PurchaseOrderNumber = Guid.NewGuid().ToString("N"), SupplierId = supplier.Id, CreatedByUserId = user.Id, Status = PurchaseOrderStatus.Submitted };
        db.AddRange(overdue, noDate); await db.SaveChangesAsync(Ct);
        var analyzer = scope.ServiceProvider.GetRequiredService<IMonitoringAnalysis>(); var inventory = await analyzer.Analyze("inventory", new(), MonitoringScope.Reports, now, Ct);
        var finding = Assert.Single(inventory.Findings, f => f.EntityId == product.Id); Assert.Equal("outOfStock", finding.Category); Assert.Equal(4, finding.Metrics["minimum"]);
        var orders = await analyzer.Analyze("orders", new(), MonitoringScope.Reports, now, Ct); Assert.Contains(orders.Findings, f => f.EntityId == overdue.Id && f.Category == "overduePurchase"); Assert.DoesNotContain(orders.Findings, f => f.EntityId == noDate.Id);
        Assert.Contains("salesOrdersHaveNoDeliveryDate", orders.Diagnostics); Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task Actual_sales_change_requires_samples_and_reports_verified_values()
    {
        await using var app = App(); _ = await Account(app); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow; var warehouse = await db.Warehouses.FirstAsync(Ct); var customer = new Customer { Name = "Synthetic monitored customer" }; db.Add(customer);
        for (var i = 0; i < 5; i++) { db.Add(new Order { OrderNumber = Guid.NewGuid().ToString("N"), CustomerId = customer.Id, WarehouseId = warehouse.Id, Status = OrderStatus.Completed, CompletedAt = now.AddDays(-2), TotalAmount = 100 }); db.Add(new Order { OrderNumber = Guid.NewGuid().ToString("N"), CustomerId = customer.Id, WarehouseId = warehouse.Id, Status = OrderStatus.Completed, CompletedAt = now.AddDays(-40), TotalAmount = 10 }); }
        await db.SaveChangesAsync(Ct); var result = await scope.ServiceProvider.GetRequiredService<IMonitoringAnalysis>().Analyze("sales", new(), MonitoringScope.Reports, now, Ct);
        Assert.Contains(result.Findings, f => f.Category == "salesChange" && f.Metrics["currentValue"] >= 500);
        Assert.Contains(result.Findings, f => f.Category == "customerPurchasingChange" && f.EntityId == customer.Id && f.Metrics["previousValue"] == 50);
    }
    [Fact]
    public async Task Explicit_review_reuses_private_execution_and_never_creates_an_order_without_approval()
    {
        await using var app = App(new(), true); var user = await Account(app); await Reset(app); await Queue(app, user.Principal); await Process(app); var alert = Assert.Single(await Alerts(app));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var sales = await db.Orders.CountAsync(Ct); var purchases = await db.PurchaseOrders.CountAsync(Ct);
        var first = (await (await user.Client.PostAsJsonAsync($"/api/operations/alerts/{alert.Id}/review", new MonitoringReviewRequest("explain", "en"), Ct)).Content.ReadFromJsonAsync<MonitoringReviewResult>(Ct))!;
        Assert.True(first.Started); Assert.NotEqual(Guid.Empty, first.ExecutionId);
        var again = (await (await user.Client.PostAsJsonAsync($"/api/operations/alerts/{alert.Id}/review", new MonitoringReviewRequest("explain", "en"), Ct)).Content.ReadFromJsonAsync<MonitoringReviewResult>(Ct))!;
        Assert.False(again.Started); Assert.Equal(first.ExecutionId, again.ExecutionId); Assert.Equal(sales, await db.Orders.CountAsync(Ct)); Assert.Equal(purchases, await db.PurchaseOrders.CountAsync(Ct));
    }
    [Fact]
    public async Task Live_proactive_inventory_alert_and_authorized_Ollama_explanation()
    {
        if (Environment.GetEnvironmentVariable("PROACTIVE_LIVE_SMOKE") != "true") Assert.Skip("Opt in with PROACTIVE_LIVE_SMOKE=true; no local model request made.");
        using var transport = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        using var http = new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan };
        var settings = OllamaTests.Settings;
        await using var app = App(ai: true, liveModel: new OllamaModelClient(settings, http), liveSettings: settings);
        var user = await Account(app); await Reset(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product { Name = "Synthetic monitored local model component", Category = new Category { Name = "Live monitor category" }, IsActive = true, Price = 10 };
        db.Add(product); await db.SaveChangesAsync(Ct); var warehouse = await db.Warehouses.FirstAsync(Ct); var balance = await db.WarehouseInventories.SingleAsync(i => i.ProductId == product.Id && i.WarehouseId == warehouse.Id, Ct); balance.MinimumStockLevel = 5; await db.SaveChangesAsync(Ct);
        var beforeOrders = await db.Orders.CountAsync(Ct); var beforePurchases = await db.PurchaseOrders.CountAsync(Ct);
        var run = await Queue(app, user.Principal); await Process(app);
        var alert = Assert.Single(await Alerts(app), a => a.EntityId == product.Id); Assert.Equal("outOfStock", alert.Category); Assert.Equal("open", alert.Status);
        await Queue(app, user.Principal); await Process(app); Assert.Single(await Alerts(app), a => a.EntityId == product.Id);
        var response = await user.Client.PostAsJsonAsync($"/api/operations/alerts/{alert.Id}/review", new MonitoringReviewRequest("explain", "en"), Ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = (await response.Content.ReadFromJsonAsync<MonitoringReviewResult>(Ct))!;
        Assert.Null(review.Error); Assert.NotNull(review.Result); Assert.Equal("model", review.Result.ResponseKind); Assert.False(review.Result.IsMock);
        var execution = await db.Set<AiExecution>().AsNoTracking().SingleAsync(e => e.Id == review.ExecutionId, Ct);
        Assert.Equal("Ollama", execution.Provider); Assert.True(execution.ProviderCalls > 0); Assert.True(execution.TotalTokens > 0);
        Assert.Equal(beforeOrders, await db.Orders.CountAsync(Ct)); Assert.Equal(beforePurchases, await db.PurchaseOrders.CountAsync(Ct));
        Console.WriteLine(JsonSerializer.Serialize(new { liveProactiveVerified = true, monitoringRun = run, alertId = alert.Id, executionId = review.ExecutionId, execution.Provider, execution.Model, execution.TotalTokens, responseKind = review.Result.ResponseKind, duplicateAlertsPrevented = true, ordersUnchanged = true }));
    }
}

public sealed class MonitoringUnitTests
{
    [Theory]
    [InlineData(200, 100, 5, 5, true)]
    [InlineData(110, 100, 5, 5, false)]
    [InlineData(200, 100, 1, 5, false)]
    [InlineData(200, 0, 5, 5, false)]
    [InlineData(100, 300, 5, 5, true)]
    public void Sales_signals_require_minimum_samples_absolute_and_relative_change(int current, int previous, int samples, int priorSamples, bool expected)
        => Assert.Equal(expected, MonitoringAnalysisService.Significant(current, previous, samples, priorSamples, new()));
    [Fact] public void Background_scope_rejects_unknown_or_user_impersonation_identity() => Assert.Throws<BusinessException>(() => new MonitoringScope("admin", "all").Demand());
    [Theory] [InlineData(0)] [InlineData(1441)] public void Invalid_frequency_is_rejected(int minutes) => Assert.Throws<BusinessException>(() => new MonitoringSettings(IntervalMinutes: minutes).Validate());
    [Fact]
    public async Task Local_inference_queue_is_bounded_and_cancellation_releases_capacity()
    {
        var gate = new LocalModelGate(1, 1); using var first = await gate.Enter(TestContext.Current.CancellationToken);
        using var stop = new CancellationTokenSource(); var queued = gate.Enter(stop.Token);
        var error = await Assert.ThrowsAsync<BusinessException>(() => gate.Enter(TestContext.Current.CancellationToken)); Assert.Equal("aiBusy", error.Code);
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued); Assert.Equal(1, gate.Pending);
    }
}
