using Demo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Demo.Api.Tests;
public sealed class MigrationTests
{
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
