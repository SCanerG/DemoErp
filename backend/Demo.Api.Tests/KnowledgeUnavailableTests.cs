using Demo.Api.AI.Knowledge;
using Demo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Demo.Api.Tests;
public sealed class KnowledgeUnavailableTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Existing_plain_PostgreSQL_keeps_ERP_schema_usable_without_vector_extension() {
        var ct=TestContext.Current.CancellationToken;using var scope=fixture.App.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await new PgKnowledgeVectorStore(db).Available(ct));Assert.False(db.Database.HasPendingModelChanges());
        Assert.NotNull(await db.Products.Take(1).ToListAsync(ct));Assert.NotNull(await db.Set<KnowledgeDocument>().Take(1).ToListAsync(ct));
        var migrations=await db.Database.GetAppliedMigrationsAsync(ct);Assert.Contains(migrations,m=>m.EndsWith("EnterpriseKnowledge"));
    }
}
