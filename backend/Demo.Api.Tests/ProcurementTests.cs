using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Api.Tests;

public sealed class ProcurementTests(ApiFixture fixture) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    private HttpClient client = null!;
    private Guid actor;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public async ValueTask InitializeAsync() { (client, actor) = await Login(UserRole.Manager); }
    public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    private async Task<(HttpClient Client, Guid Id)> Login(UserRole role)
    {
        using var scope = fixture.App.Services.CreateScope(); var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var email = $"procurement-{Guid.NewGuid():N}@example.com";
        var user = (await auth.Register(new("Purchaser", email, "ProcurementTest123!"), Ct))!;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Users.FindAsync([user.Id], Ct))!.Role = role; await db.SaveChangesAsync(Ct);
        var token = (await auth.Login(new(email, "ProcurementTest123!"), Ct))!; var http = fixture.App.CreateClient(); http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken); return (http, user.Id);
    }
    private async Task<(SupplierResponse Supplier, Product Product)> Seed()
    {
        var supplier = await client.PostAsJsonAsync("/api/suppliers", new SupplierRequest($"s-{Guid.NewGuid():N}", "Parts supplier", "Private contact", "contact@example.com", "555", "Private address", "Tax-private"), Ct);
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Name = "Procurement" }; var product = new Product { Name = "Purchased part", CategoryId = category.Id, Price = 99, IsActive = true }; db.AddRange(category, product); await db.SaveChangesAsync(Ct);
        return ((await supplier.Content.ReadFromJsonAsync<SupplierResponse>(Json, Ct))!, product);
    }
    private async Task<PurchaseResponse> Create(Guid supplier, params (Guid Product, int Qty, decimal Cost)[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/purchase-orders", new PurchaseRequest(supplier, null, "Private notes", lines.Select(i => new PurchaseItemRequest(i.Product, i.Qty, i.Cost)).ToList()), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<PurchaseResponse>(Json, Ct))!;
    }
    private async Task<PurchaseResponse> Read(Guid id) => (await client.GetFromJsonAsync<PurchaseResponse>($"/api/purchase-orders/{id}", Json, Ct))!;
    private async Task Submit(Guid id) => Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{id}/submit", null, Ct)).StatusCode);
    private Task<HttpResponseMessage> Receive(PurchaseResponse order, Guid key, params (Guid Item, int Qty)[] lines) => SendReceipt(client, order.Id, key, new(lines.Select(i => new ReceiptItemRequest(i.Item, i.Qty)).ToList(), "Goods arrived"));
    private static Task<HttpResponseMessage> SendReceipt(HttpClient http, Guid id, Guid? key, ReceiptRequest input)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{id}/receipts") { Content = JsonContent.Create(input) }; if (key.HasValue) message.Headers.Add("Idempotency-Key", key.Value.ToString()); return http.SendAsync(message, Ct);
    }
    private async Task<int> Stock(Guid product) => (await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{product}", Ct))!.QuantityOnHand;

    [Theory]
    [InlineData("GET", "/suppliers")][InlineData("POST", "/suppliers")][InlineData("GET", "/purchase-orders")][InlineData("POST", "/purchase-orders")]
    [InlineData("GET", "/goods-receipts/id")][InlineData("POST", "/purchase-orders/id/receipts")][InlineData("GET", "/dashboard/procurement-summary")][InlineData("GET", "/reports/procurement")]
    public async Task Anonymous_is_rejected(string method, string path)
    { using var http = fixture.App.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(new(new(method), "/api" + path.Replace("id", Guid.NewGuid().ToString())), Ct)).StatusCode); }

    [Theory]
    [InlineData("POST", "/suppliers")][InlineData("PUT", "/suppliers/id")][InlineData("PUT", "/suppliers/id/status")][InlineData("POST", "/purchase-orders")]
    [InlineData("PUT", "/purchase-orders/id")][InlineData("POST", "/purchase-orders/id/submit")][InlineData("POST", "/purchase-orders/id/cancel")][InlineData("POST", "/purchase-orders/id/receipts")]
    public async Task Viewer_cannot_mutate(string method, string path)
    { var login = await Login(UserRole.Viewer); using var http = login.Client; Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(new(new(method), "/api" + path.Replace("id", Guid.NewGuid().ToString())) { Content = JsonContent.Create(new { }) }, Ct)).StatusCode); }

    [Theory]
    [InlineData("", "Name", "")][InlineData("bad code", "Name", "")][InlineData("valid", "", "")][InlineData("valid", "Name", "invalid-email")]
    public async Task Supplier_validation(string code, string name, string email) => Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/suppliers", new SupplierRequest(code, name, "", email, "", "", ""), Ct)).StatusCode);

    [Fact]
    public async Task Supplier_normalization_unique_edit_status_and_paged_read()
    {
        var s = await Seed(); var code = s.Supplier.SupplierCode;
        Assert.Equal(code.ToUpperInvariant(), code);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/suppliers", new SupplierRequest($" {code.ToLowerInvariant()} ", "Duplicate", "", "", "", "", ""), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/suppliers/{s.Supplier.Id}", new SupplierRequest(code, "Changed", "", "", "", "", ""), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/suppliers/{s.Supplier.Id}/status", new { isActive = false }, Ct)).StatusCode);
        var page = (await client.GetFromJsonAsync<ReportPage<SupplierResponse>>($"/api/suppliers?search={code}&isActive=false&pageSize=1", Json, Ct))!;
        Assert.Equal("Changed", Assert.Single(page.Items).Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/suppliers?pageSize=101", Ct)).StatusCode);
    }
    [Theory]
    [InlineData(0, 1)][InlineData(-1, 1)][InlineData(100001, 1)][InlineData(2, -1)][InlineData(2, 1.001)]
    public async Task Invalid_purchase_lines_are_rejected(int quantity, decimal cost)
    { var s = await Seed(); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/purchase-orders", new PurchaseRequest(s.Supplier.Id, null, "", [new(s.Product.Id, quantity, cost)]), Ct)).StatusCode); }

    [Fact]
    public async Task Draft_totals_use_entered_cost_edit_preserves_item_identity_and_submit_changes_no_stock()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 3, 1.25m)); Assert.Equal(3.75m, o.TotalAmount); Assert.Equal(0, await Stock(s.Product.Id));
        var changed = await client.PutAsJsonAsync($"/api/purchase-orders/{o.Id}", new PurchaseRequest(s.Supplier.Id, null, "Changed", [new(s.Product.Id, 4, 2m)]), Ct); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = await Read(o.Id); Assert.Equal(8, updated.TotalAmount); Assert.Equal(o.Items[0].Id, updated.Items[0].Id);
        await Submit(o.Id); Assert.Equal(0, await Stock(s.Product.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/purchase-orders/{o.Id}", new PurchaseRequest(s.Supplier.Id, null, "", [new(s.Product.Id, 4, 2m)]), Ct)).StatusCode);
    }
    [Fact]
    public async Task Missing_inactive_and_duplicate_products_or_suppliers_are_rejected()
    {
        var s = await Seed();
        foreach (var request in new[] { new PurchaseRequest(Guid.NewGuid(), null, "", [new(s.Product.Id, 1, 1)]), new PurchaseRequest(s.Supplier.Id, null, "", [new(Guid.NewGuid(), 1, 1)]), new PurchaseRequest(s.Supplier.Id, null, "", [new(s.Product.Id, 1, 1), new(s.Product.Id, 2, 1)]), new PurchaseRequest(s.Supplier.Id, null, "", []) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/purchase-orders", request, Ct)).StatusCode);
        var o = await Create(s.Supplier.Id, (s.Product.Id, 2, 1)); await client.PutAsJsonAsync($"/api/suppliers/{s.Supplier.Id}/status", new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/purchase-orders/{o.Id}/submit", null, Ct)).StatusCode);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Products.FindAsync([s.Product.Id], Ct))!.IsActive = false; await db.SaveChangesAsync(Ct);
        await client.PutAsJsonAsync($"/api/suppliers/{s.Supplier.Id}/status", new { isActive = true }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/purchase-orders/{o.Id}/submit", null, Ct)).StatusCode);
    }
    [Fact]
    public async Task Partial_then_full_receipt_has_atomic_stock_movements_actor_and_allowlisted_audit()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 10, 2)); await Submit(o.Id);
        var first = await Receive(o, Guid.NewGuid(), (o.Items[0].Id, 6)); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var receipt = (await first.Content.ReadFromJsonAsync<ReceiptResponse>(Json, Ct))!; Assert.Equal(actor, receipt.CreatedByUserId); Assert.Equal(6, await Stock(s.Product.Id)); Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await Read(o.Id)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/purchase-orders/{o.Id}/cancel", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Receive(o, Guid.NewGuid(), (o.Items[0].Id, 4))).StatusCode); Assert.Equal(10, await Stock(s.Product.Id)); Assert.Equal(PurchaseOrderStatus.Received, (await Read(o.Id)).Status);
        var movements = (await client.GetFromJsonAsync<List<MovementResponse>>($"/api/inventory/{s.Product.Id}/movements", Json, Ct))!; Assert.Equal(2, movements.Count); Assert.All(movements, m => { Assert.Equal(MovementType.PurchaseReceipt, m.MovementType); Assert.Equal(actor, m.CreatedByUserId); Assert.Null(m.ReferenceId); Assert.NotNull(m.GoodsReceiptId); });
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var audit = await db.AuditLogs.SingleAsync(a => a.EntityId == receipt.Id && a.Action == "GoodsReceiptPost", Ct); Assert.Equal(actor, audit.UserId); Assert.DoesNotContain("Goods arrived", audit.NewValues ?? ""); Assert.DoesNotContain("contact@example", string.Join("", await db.AuditLogs.Where(a => a.EntityId == s.Supplier.Id).Select(a => a.NewValues).ToListAsync(Ct)));
    }
    [Theory]
    [InlineData(0, 400)][InlineData(-1, 400)][InlineData(11, 409)]
    public async Task Receipt_invalid_or_over_remaining_does_not_change_stock(int quantity, int status)
    { var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 10, 1)); await Submit(o.Id); Assert.Equal((HttpStatusCode)status, (await Receive(o, Guid.NewGuid(), (o.Items[0].Id, quantity))).StatusCode); Assert.Equal(0, await Stock(s.Product.Id)); }
    [Fact]
    public async Task Wrong_duplicate_empty_items_and_missing_key_are_rejected()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 10, 1)); await Submit(o.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await Receive(o, Guid.NewGuid(), (Guid.NewGuid(), 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Receive(o, Guid.NewGuid(), (o.Items[0].Id, 1), (o.Items[0].Id, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Receive(o, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendReceipt(client, o.Id, null, new([new(o.Items[0].Id, 1)], ""))).StatusCode);
        Assert.Equal(0, await Stock(s.Product.Id));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Draft_and_cancelled_cannot_receive(bool cancelled)
    { var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 1, 1)); if (cancelled) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{o.Id}/cancel", null, Ct)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await Receive(o, Guid.NewGuid(), (o.Items[0].Id, 1))).StatusCode); }
    [Fact]
    public async Task Concurrent_receipts_cannot_over_receive_remaining_quantity()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 100, 1)); await Submit(o.Id); await Receive(o, Guid.NewGuid(), (o.Items[0].Id, 60));
        var results = await Task.WhenAll(Receive(o, Guid.NewGuid(), (o.Items[0].Id, 30)), Receive(o, Guid.NewGuid(), (o.Items[0].Id, 30)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); Assert.Equal(90, await Stock(s.Product.Id)); Assert.Equal(90, (await Read(o.Id)).Items[0].QuantityReceived);
    }
    [Fact]
    public async Task Parallel_identical_retries_replay_one_receipt_even_after_full_receipt()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 10, 1)); await Submit(o.Id); var key = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Receive(o, key, (o.Items[0].Id, 10)))); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = new List<Guid>(); foreach (var r in results) ids.Add((await r.Content.ReadFromJsonAsync<ReceiptResponse>(Json, Ct))!.Id); Assert.Single(ids.Distinct()); Assert.Equal(10, await Stock(s.Product.Id));
        Assert.Equal(HttpStatusCode.OK, (await Receive(o, key, (o.Items[0].Id, 10))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Receive(o, key, (o.Items[0].Id, 1))).StatusCode);
        var another = await Create(s.Supplier.Id, (s.Product.Id, 10, 1)); await Submit(another.Id); Assert.Equal(HttpStatusCode.Conflict, (await Receive(another, key, (another.Items[0].Id, 10))).StatusCode);
    }
    [Fact]
    public async Task Failed_validation_does_not_consume_key_and_different_users_can_reuse_key()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 10, 1)); await Submit(o.Id); var key = Guid.NewGuid(); Assert.Equal(HttpStatusCode.Conflict, (await Receive(o, key, (o.Items[0].Id, 11))).StatusCode); Assert.Equal(HttpStatusCode.Created, (await Receive(o, key, (o.Items[0].Id, 5))).StatusCode);
        var login = await Login(UserRole.Admin); using var admin = login.Client; Assert.Equal(HttpStatusCode.Created, (await SendReceipt(admin, o.Id, key, new([new(o.Items[0].Id, 5)], "Goods arrived"))).StatusCode); Assert.Equal(10, await Stock(s.Product.Id));
    }
    [Fact]
    public async Task Multiple_products_and_multiple_orders_lock_consistently()
    {
        var a = await Seed(); var b = await Seed(); var first = await Create(a.Supplier.Id, (a.Product.Id, 10, 1), (b.Product.Id, 20, 2)); var second = await Create(a.Supplier.Id, (b.Product.Id, 20, 2), (a.Product.Id, 10, 1)); await Submit(first.Id); await Submit(second.Id);
        var results = await Task.WhenAll(Receive(first, Guid.NewGuid(), first.Items.Select(i => (i.Id, i.QuantityOrdered)).ToArray()), Receive(second, Guid.NewGuid(), second.Items.Select(i => (i.Id, i.QuantityOrdered)).ToArray())); Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode)); Assert.Equal(20, await Stock(a.Product.Id)); Assert.Equal(40, await Stock(b.Product.Id));
    }
    [Fact]
    public async Task Concurrent_purchase_numbers_are_unique()
    { var s = await Seed(); var orders = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Create(s.Supplier.Id, (s.Product.Id, 1, 0)))); Assert.Equal(8, orders.Select(o => o.PurchaseOrderNumber).Distinct().Count()); Assert.All(orders, o => Assert.Equal(0, o.TotalAmount)); }
    [Fact]
    public async Task Receipt_immutability_and_supplier_history_restrict_deletion()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 1, 1)); await Submit(o.Id); var result = await Receive(o, Guid.NewGuid(), (o.Items[0].Id, 1)); var r = (await result.Content.ReadFromJsonAsync<ReceiptResponse>(Json, Ct))!;
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GoodsReceipts\" SET \"Notes\" = 'changed' WHERE \"Id\" = {r.Id}", Ct));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"GoodsReceiptItems\" WHERE \"GoodsReceiptId\" = {r.Id}", Ct));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Suppliers\" WHERE \"Id\" = {s.Supplier.Id}", Ct));
    }
    [Fact]
    public async Task Audit_failure_rolls_back_receipt_items_inventory_movements_status_and_key()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 10, 1)); await Submit(o.Id); var key = Guid.NewGuid();
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_receipt_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW."Action" = 'GoodsReceiptPost' THEN RAISE EXCEPTION 'test audit failure'; END IF; RETURN NEW; END; $$;
            CREATE TRIGGER reject_receipt_test BEFORE INSERT ON "AuditLogs" FOR EACH ROW EXECUTE FUNCTION reject_receipt_test();
            """, Ct);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Receive(o, key, (o.Items[0].Id, 10))).StatusCode); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_receipt_test ON \"AuditLogs\"; DROP FUNCTION reject_receipt_test();", Ct); }
        Assert.Equal(0, await Stock(s.Product.Id)); Assert.Equal(PurchaseOrderStatus.Submitted, (await Read(o.Id)).Status); Assert.False(await db.GoodsReceipts.AnyAsync(r => r.PurchaseOrderId == o.Id, Ct)); Assert.False(await db.InventoryMovements.AnyAsync(m => m.ProductId == s.Product.Id, Ct)); Assert.Equal(HttpStatusCode.Created, (await Receive(o, key, (o.Items[0].Id, 10))).StatusCode);
    }
    [Fact]
    public async Task Revocation_is_checked_for_procurement_and_viewer_reads_are_allowed()
    {
        var login = await Login(UserRole.Viewer); using var http = login.Client;
        foreach (var path in new[] { "/suppliers", "/purchase-orders", "/reports/procurement", "/dashboard/procurement-summary" }) Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api" + path, Ct)).StatusCode);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Users.FindAsync([login.Id], Ct))!.IsActive = false; await db.SaveChangesAsync(Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/suppliers", Ct)).StatusCode);
    }

    [Fact]
    public async Task Multi_product_overflow_rolls_back_previously_staged_inventory_and_all_receipt_rows()
    {
        var a = await Seed(); var b = await Seed(); var ordered = new[] { a.Product.Id, b.Product.Id }.Order().ToArray();
        var o = await Create(a.Supplier.Id, (ordered[0], 2, 1), (ordered[1], 2, 1)); await Submit(o.Id);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WarehouseInventories\" SET \"QuantityOnHand\" = {int.MaxValue} WHERE \"ProductId\" = {ordered[1]}", Ct);
        var result = await Receive(o, Guid.NewGuid(), o.Items.Select(i => (i.Id, 2)).ToArray()); Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(0, await Stock(ordered[0])); Assert.Equal(int.MaxValue, await Stock(ordered[1])); Assert.All((await Read(o.Id)).Items, i => Assert.Equal(0, i.QuantityReceived)); Assert.False(await db.GoodsReceipts.AnyAsync(r => r.PurchaseOrderId == o.Id, Ct));
    }
    [Fact]
    public async Task Concurrent_changed_payload_with_one_key_commits_exactly_one_operation()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 20, 1)); await Submit(o.Id); var key = Guid.NewGuid();
        var results = await Task.WhenAll(Receive(o, key, (o.Items[0].Id, 3)), Receive(o, key, (o.Items[0].Id, 7))); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); Assert.Contains(await Stock(s.Product.Id), new[] { 3, 7 });
    }
    [Fact]
    public async Task Reordered_items_and_trimmed_notes_are_the_same_canonical_operation()
    {
        var a = await Seed(); var b = await Seed(); var o = await Create(a.Supplier.Id, (a.Product.Id, 1, 1), (b.Product.Id, 2, 1)); await Submit(o.Id); var key = Guid.NewGuid();
        var input = new ReceiptRequest(o.Items.Select(i => new ReceiptItemRequest(i.Id, i.QuantityOrdered)).ToList(), "  Arrived  "); var first = await SendReceipt(client, o.Id, key, input); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var replay = await SendReceipt(client, o.Id, key, new(input.Items.AsEnumerable().Reverse().ToList(), "Arrived")); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal((await first.Content.ReadFromJsonAsync<ReceiptResponse>(Json, Ct))!.Id, (await replay.Content.ReadFromJsonAsync<ReceiptResponse>(Json, Ct))!.Id);
    }
    [Fact]
    public async Task Submitted_unreceived_order_can_cancel_without_stock_changes_and_cannot_reopen()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 2, 1)); await Submit(o.Id); Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{o.Id}/cancel", null, Ct)).StatusCode); Assert.Equal(0, await Stock(s.Product.Id)); Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/purchase-orders/{o.Id}/submit", null, Ct)).StatusCode);
    }
    [Fact]
    public async Task Procurement_reports_filter_and_summary_compute_real_database_quantities()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 7, 3)); await Submit(o.Id); await Receive(o, Guid.NewGuid(), (o.Items[0].Id, 2));
        var page = (await client.GetFromJsonAsync<ReportPage<PurchaseSummary>>($"/api/reports/procurement?supplierId={s.Supplier.Id}&status=PartiallyReceived&pageSize=1", Json, Ct))!; var row = Assert.Single(page.Items); Assert.Equal(7, row.OrderedQuantity); Assert.Equal(2, row.ReceivedQuantity); Assert.Equal(5, row.RemainingQuantity);
        var summary = (await client.GetFromJsonAsync<ProcurementSummary>("/api/dashboard/procurement-summary", Ct))!; Assert.True(summary.PartiallyReceivedOrders >= 1); Assert.True(summary.OutstandingQuantity >= 5);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/procurement?status=Unknown", Ct)).StatusCode);
    }
    [Fact]
    public async Task Separate_api_instances_share_postgres_idempotency_and_row_locks()
    {
        var s = await Seed(); var o = await Create(s.Supplier.Id, (s.Product.Id, 10, 1)); await Submit(o.Id);
        using var scope = fixture.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var otherApp = new TestApplication(db.Database.GetConnectionString()!); using var other = otherApp.CreateClient(); other.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var key = Guid.NewGuid(); var input = new ReceiptRequest([new(o.Items[0].Id, 10)], "Same delivery");
        var results = await Task.WhenAll(SendReceipt(client, o.Id, key, input), SendReceipt(other, o.Id, key, input)); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Equal(10, await Stock(s.Product.Id));
        var second = await Create(s.Supplier.Id, (s.Product.Id, 100, 1)); await Submit(second.Id); await Receive(second, Guid.NewGuid(), (second.Items[0].Id, 60));
        var request = new ReceiptRequest([new(second.Items[0].Id, 30)], "Another delivery"); var competing = await Task.WhenAll(SendReceipt(client, second.Id, Guid.NewGuid(), request), SendReceipt(other, second.Id, Guid.NewGuid(), request)); Assert.Single(competing, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(competing, r => r.StatusCode == HttpStatusCode.Conflict); Assert.Equal(100, await Stock(s.Product.Id));
    }
}
