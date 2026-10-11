using Demo.Api.Contracts;
using Demo.Api.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Demo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Demo.Api.Tests;
public sealed class MigrationTests
{
    [Fact]
    public async Task Procurement_upgrade_preserves_all_existing_rows_and_immutable_movement_history()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("procurement_upgrade").WithUsername("catalog").WithPassword("test-only-password").Build(); await database.StartAsync(ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261009133445_ReportingIndexes", ct);
        var user = Guid.NewGuid(); var category = Guid.NewGuid(); var product = Guid.NewGuid(); var customer = Guid.NewGuid(); var order = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\",\"Name\",\"Email\",\"PasswordHash\",\"CreatedAt\",\"Role\",\"IsActive\",\"SecurityVersion\",\"IsBootstrapAccount\") VALUES ({user},'Legacy','legacy@example.com','preserved-hash',{now},'Viewer',true,0,false)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Categories\" (\"Id\",\"Name\",\"Description\",\"IsActive\",\"CreatedAt\") VALUES ({category},'Legacy','',true,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\",\"CategoryId\",\"Name\",\"Description\",\"Price\",\"IsActive\",\"CreatedAt\") VALUES ({product},{category},'Legacy product','',12.50,true,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Customers\" (\"Id\",\"Name\",\"Email\",\"Phone\",\"Address\",\"IsActive\",\"CreatedAt\") VALUES ({customer},'Legacy','','','',true,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Orders\" (\"Id\",\"CustomerId\",\"OrderNumber\",\"Status\",\"OrderDate\",\"TotalAmount\",\"CreatedAt\") VALUES ({order},{customer},'LEGACY-PROC','Confirmed',{now},25,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OrderItems\" (\"Id\",\"OrderId\",\"ProductId\",\"Quantity\",\"UnitPrice\",\"LineTotal\") VALUES ({Guid.NewGuid()},{order},{product},2,12.50,25)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Inventories\" SET \"QuantityOnHand\"=8 WHERE \"ProductId\"={product}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"InventoryMovements\" (\"Id\",\"ProductId\",\"MovementType\",\"Quantity\",\"QuantityBefore\",\"QuantityAfter\",\"ReferenceType\",\"ReferenceId\",\"Reason\",\"CreatedAt\",\"CreatedByUserId\") VALUES ({Guid.NewGuid()},{product},'OrderDeduction',2,10,8,'Order',{order},'Legacy deduction',{now},{user})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AuditLogs\" (\"Id\",\"UserId\",\"UserName\",\"Action\",\"EntityName\",\"EntityId\",\"Description\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{user},'Legacy','OrderConfirm','Order',{order},'OrderConfirm',{now})", ct);
        string[] tables = ["Users", "Categories", "Products", "Customers", "Orders", "OrderItems", "Inventories", "InventoryMovements", "AuditLogs"];
        async Task<string[]> Signatures()
        {
            var result = new List<string>();
            foreach (var table in tables)
            {
                // Identifiers come exclusively from the fixed table allowlist above.
                var removed = table == "InventoryMovements" ? "-'GoodsReceiptId'-'WarehouseId'-'StockTransferId'-'ProductionOrderId'" : "-'WarehouseId'-'FingerprintVersion'-'ProductType'";
                var sql = $"SELECT md5(COALESCE(string_agg((to_jsonb(t){removed})::text,'' ORDER BY t.\"Id\"),'')) AS \"Value\" FROM \"{table}\" t";
                result.Add(await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct));
            }
            return result.ToArray();
        }
        var before = await Signatures(); await migrator.MigrateAsync("20261010072904_Procurement", ct); Assert.Equal(before, await Signatures());
        var supplier = Guid.NewGuid(); var purchase = Guid.NewGuid(); var purchaseItem = Guid.NewGuid(); var receipt = Guid.NewGuid();
        var receiptKey = Guid.NewGuid(); var receiptInput = new ReceiptRequest([new(purchaseItem,2)],"Preserved receipt");
        var legacyCanonical = JsonSerializer.Serialize(new {purchaseOrderId=purchase,notes=receiptInput.Notes,items=receiptInput.Items.OrderBy(i=>i.PurchaseOrderItemId).ToArray()});
        var legacyFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyCanonical)));
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Suppliers\" (\"Id\",\"SupplierCode\",\"Name\",\"ContactPerson\",\"Email\",\"Phone\",\"Address\",\"TaxNumber\",\"IsActive\",\"CreatedAt\") VALUES ({supplier},'LEGACY','Legacy supplier','','','','','',true,{now})",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PurchaseOrders\" (\"Id\",\"PurchaseOrderNumber\",\"SupplierId\",\"Status\",\"OrderDate\",\"TotalAmount\",\"Notes\",\"CreatedAt\",\"CreatedByUserId\") VALUES ({purchase},'PO-LEGACY',{supplier},'Received',{now},20,'',{now},{user})",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PurchaseOrderItems\" (\"Id\",\"PurchaseOrderId\",\"ProductId\",\"QuantityOrdered\",\"QuantityReceived\",\"UnitCost\",\"LineTotal\") VALUES ({purchaseItem},{purchase},{product},2,2,10,20)",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"GoodsReceipts\" (\"Id\",\"ReceiptNumber\",\"PurchaseOrderId\",\"ReceivedAt\",\"Notes\",\"CreatedByUserId\",\"CreatedAt\",\"IdempotencyKey\",\"PayloadFingerprint\") VALUES ({receipt},'GR-LEGACY',{purchase},{now},'Preserved receipt',{user},{now},{receiptKey},{legacyFingerprint})",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"GoodsReceiptItems\" (\"Id\",\"GoodsReceiptId\",\"PurchaseOrderItemId\",\"QuantityReceived\") VALUES ({Guid.NewGuid()},{receipt},{purchaseItem},2)",ct);
        // The opening stock already includes historic receipts; do not replay them.
        tables = [.. tables, "Suppliers", "PurchaseOrders", "PurchaseOrderItems", "GoodsReceipts", "GoodsReceiptItems"];
        var phase7 = await Signatures(); await migrator.MigrateAsync("20261010081430_MultiWarehouse", ct); Assert.Equal(phase7,await Signatures());
        string[] phase8Tables = ["Warehouses", "WarehouseInventories", "StockTransfers", "StockTransferItems", "LegacyInventories"];
        async Task<string> Phase8Hash(string table)
        {
            Assert.Contains(table,phase8Tables); // identifiers from this fixed allowlist only
            var sql=$"SELECT md5(COALESCE(string_agg(to_jsonb(t)::text,'' ORDER BY t.\"Id\"),'')) AS \"Value\" FROM \"{table}\" t";
            return await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
        }
        var phase8Hashes = new List<string>();
        foreach (var table in phase8Tables) phase8Hashes.Add(await Phase8Hash(table));
        await migrator.MigrateAsync(cancellationToken: ct); Assert.Equal(phase7,await Signatures());
        for (var i=0;i<phase8Tables.Length;i++) Assert.Equal(phase8Hashes[i],await Phase8Hash(phase8Tables[i]));
        Assert.Equal("RawMaterial", await db.Database.SqlQuery<string>($"SELECT \"ProductType\" AS \"Value\" FROM \"Products\" WHERE \"Id\"={product}").SingleAsync(ct));
        await migrator.MigrateAsync(cancellationToken: ct); Assert.Equal(phase7,await Signatures());
        var main = await db.Warehouses.SingleAsync(w=>w.IsDefault,ct); Assert.Equal("MAIN",main.Code);
        var opening = await db.WarehouseInventories.SingleAsync(i=>i.ProductId==product,ct); Assert.Equal(main.Id,opening.WarehouseId); Assert.Equal(8,opening.QuantityOnHand);
        Assert.Equal(8,(await db.Inventories.SingleAsync(i=>i.ProductId==product,ct)).QuantityOnHand); Assert.Single(await db.InventoryMovements.ToListAsync(ct));
        var oldReceipt = await db.GoodsReceipts.SingleAsync(ct); Assert.Equal(main.Id,oldReceipt.WarehouseId); Assert.Equal(1,oldReceipt.FingerprintVersion); Assert.Equal(legacyFingerprint,oldReceipt.PayloadFingerprint);
        var replay = await new ProcurementService(db).Receive(purchase,receiptInput,user,receiptKey,ct); Assert.True(replay.Replay); Assert.Equal(receipt,replay.Receipt.Id); Assert.Equal(main.Id,replay.Receipt.WarehouseId);
        Assert.Equal(phase7,await Signatures());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(order, (await db.InventoryMovements.SingleAsync(ct)).ReferenceId); Assert.Null((await db.InventoryMovements.SingleAsync(ct)).GoodsReceiptId);
    }
    [Fact]
    public async Task Inventory_upgrade_preserves_existing_orders_and_backfills_zero_without_invented_movements()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("inventory_upgrade_test").WithUsername("catalog").WithPassword("test-only-password").Build();
        await database.StartAsync(ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261007205005_BusinessWorkflow", ct);
        var category = Guid.NewGuid(); var customer = Guid.NewGuid(); var product = Guid.NewGuid(); var order = Guid.NewGuid(); var item = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Categories\" (\"Id\",\"Name\",\"Description\",\"IsActive\",\"CreatedAt\") VALUES ({category},'Legacy category','',true,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Customers\" (\"Id\",\"Name\",\"Email\",\"Phone\",\"Address\",\"IsActive\",\"CreatedAt\") VALUES ({customer},'Legacy customer','','','',true,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\",\"CategoryId\",\"Name\",\"Description\",\"Price\",\"IsActive\",\"CreatedAt\") VALUES ({product},{category},'Legacy product','',12.50,true,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Orders\" (\"Id\",\"CustomerId\",\"OrderNumber\",\"Status\",\"OrderDate\",\"TotalAmount\",\"CreatedAt\") VALUES ({order},{customer},'LEGACY-001','Confirmed',{now},25.00,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OrderItems\" (\"Id\",\"OrderId\",\"ProductId\",\"Quantity\",\"UnitPrice\",\"LineTotal\") VALUES ({item},{order},{product},2,12.50,25.00)", ct);
        await migrator.MigrateAsync(cancellationToken: ct);
        Assert.Equal(0, (await db.Inventories.SingleAsync(i => i.ProductId == product, ct)).QuantityOnHand);
        Assert.Equal(0, await db.InventoryMovements.CountAsync(ct));
        Assert.Equal(Demo.Api.Domain.OrderStatus.Confirmed, (await db.Orders.SingleAsync(o => o.Id == order, ct)).Status);
        Assert.Equal(25m, (await db.OrderItems.SingleAsync(i => i.Id == item, ct)).LineTotal);
        var newProduct = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\",\"CategoryId\",\"Name\",\"Description\",\"Price\",\"IsActive\",\"CreatedAt\") VALUES ({newProduct},{category},'New SQL product','',1,true,{now})", ct);
        Assert.Equal(0, (await db.Inventories.SingleAsync(i => i.ProductId == newProduct, ct)).QuantityOnHand);
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task Reporting_upgrade_preserves_legacy_completed_order_without_fabricating_completion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("reporting_upgrade_test").WithUsername("catalog").WithPassword("test-only-password").Build();
        await database.StartAsync(ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261009122237_SecurityAndAudit", ct);
        var customer = Guid.NewGuid(); var order = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Customers\" (\"Id\",\"Name\",\"Email\",\"Phone\",\"Address\",\"IsActive\",\"CreatedAt\") VALUES ({customer},'Legacy','','','',true,{now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Orders\" (\"Id\",\"CustomerId\",\"OrderNumber\",\"Status\",\"OrderDate\",\"TotalAmount\",\"CreatedAt\") VALUES ({order},{customer},'LEGACY-REPORT','Completed',{now},100,{now})", ct);
        await migrator.MigrateAsync(cancellationToken: ct);
        var row = await db.Orders.SingleAsync(o => o.Id == order, ct);
        Assert.Null(row.CompletedAt); Assert.Equal(100, row.TotalAmount); Assert.Equal(Demo.Api.Domain.OrderStatus.Completed, row.Status);
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task Existing_products_survive_the_required_category_upgrade()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("upgrade_test").WithUsername("catalog").WithPassword("test-only-password").Build();
        await database.StartAsync(ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261007185344_InitialCreate", ct);
        var id = Guid.NewGuid(); var created = DateTimeOffset.UtcNow;
        // Fixture data in the old schema; schema changes themselves are exclusively EF migrations.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Products\" (\"Id\", \"Name\", \"Description\", \"Price\", \"IsActive\", \"CreatedAt\") VALUES ({id}, 'Legacy product', '', 12.50, TRUE, {created})", ct);
        await migrator.MigrateAsync(cancellationToken: ct);
        var product = await db.Products.AsNoTracking().Include(p => p.Category).SingleAsync(p => p.Id == id, ct);
        Assert.Equal("Legacy product", product.Name); Assert.Equal(12.50m, product.Price);
        Assert.Equal("General", product.Category.Name); Assert.NotEqual(Guid.Empty, product.CategoryId);
        Assert.Equal(1, await db.Products.CountAsync(ct));
        var inventory = await db.Inventories.SingleAsync(i => i.ProductId == id, ct);
        Assert.Equal(0, inventory.QuantityOnHand); Assert.Equal(0, inventory.MinimumStockLevel);
        Assert.Equal(0, await db.InventoryMovements.CountAsync(ct));
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
