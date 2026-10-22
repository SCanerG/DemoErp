using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.AI.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
namespace Demo.Api.Tests;
public sealed class WorkflowMigrationTests
{
    [Fact]
    public async Task Secure_workflow_migration_preserves_existing_business_rows_and_credentials() {
        var ct=TestContext.Current.CancellationToken;
        await using var postgres=new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("workflow_migration").WithUsername("catalog").WithPassword("test-only-migration-password").Build();await postgres.StartAsync(ct);
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261020223656_AgentControlCenter",ct);
        var user=new User{Name="Migration owner",Email="migration@example.com",PasswordHash="preserved-test-hash",Role=UserRole.Admin};
        var category=new Category{Name="Preserved category"};var product=new Product{Name="Preserved product",CategoryId=category.Id,Price=12,IsActive=true};var customer=new Customer{Name="Preserved customer"};db.AddRange(user,category,product,customer);await db.SaveChangesAsync(ct);
        var balance=await db.WarehouseInventories.SingleAsync(i=>i.ProductId==product.Id,ct);balance.QuantityOnHand=8;
        var order=new Order{CustomerId=customer.Id,WarehouseId=balance.WarehouseId,OrderNumber="WF-MIGRATION",TotalAmount=24,Items=[new(){ProductId=product.Id,Quantity=2,UnitPrice=12,LineTotal=24}]};db.Add(order);await db.SaveChangesAsync(ct);
        var audits=await db.AuditLogs.CountAsync(ct);await db.GetService<IMigrator>().MigrateAsync(cancellationToken:ct);db.ChangeTracker.Clear();
        Assert.Equal("preserved-test-hash",(await db.Users.SingleAsync(u=>u.Id==user.Id,ct)).PasswordHash);Assert.Equal(24,(await db.Orders.SingleAsync(o=>o.Id==order.Id,ct)).TotalAmount);
        Assert.Equal(8,(await db.WarehouseInventories.SingleAsync(i=>i.ProductId==product.Id,ct)).QuantityOnHand);Assert.Equal(audits,await db.AuditLogs.CountAsync(ct));Assert.Empty(await db.Set<AiOrderProposal>().ToListAsync(ct));
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
