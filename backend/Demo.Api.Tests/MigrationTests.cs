using Demo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Demo.Api.Tests;
public sealed class MigrationTests
{
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
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
