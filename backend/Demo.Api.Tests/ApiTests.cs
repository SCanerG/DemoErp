using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Demo.Api.Contracts;
using Demo.Api.Data;
using Demo.Api.Domain;
using Demo.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Demo.Api.Tests;

public sealed class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
        { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private const string Password = "TestPassword123!";
    private static string Email() => $"{Guid.NewGuid():N}@example.com";

    [Fact]
    public void Startup_rejects_a_short_signing_secret()
    {
        using var invalid = fixture.App.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "short" })));
        Assert.Throws<OptionsValidationException>(() => invalid.CreateClient());
    }

    [Fact]
    public void Startup_rejects_the_development_signing_secret_in_production()
    {
        using var invalid = fixture.App.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Jwt:Secret"] = "development-only-change-me-jwt-signing-key-32bytes" }));
        });
        Assert.Throws<OptionsValidationException>(() => invalid.CreateClient());
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    public async Task Bearer_validation_rejects_invalid_claims_signatures_and_expired_tokens(string invalid)
    {
        var key = invalid == "signature" ? "incorrect-test-key-with-at-least-32-bytes" : "test-only-signing-key-with-at-least-32-bytes";
        var token = new JwtSecurityToken(
            issuer: invalid == "issuer" ? "wrong" : "catalog-tests",
            audience: invalid == "audience" ? "wrong" : "catalog-tests",
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: invalid == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        using var client = fixture.App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products", TestContext.Current.CancellationToken)).StatusCode);
    }
    private async Task<HttpClient> AuthorizedClient()
    {
        var email = Email();
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Tester", email, Password), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Email == email, TestContext.Current.CancellationToken)).Role = UserRole.Admin;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var login = await fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password), cancellationToken: TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<LoginResponse>(Json, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        var client = fixture.App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    [Fact]
    public async Task Registration_validates_normalizes_and_hashes_without_exposing_credentials()
    {
        var invalid = await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(" ", "bad", "short"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("errors", await invalid.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken));
        var email = Email();
        var response = await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(" Tester ", email.ToUpperInvariant(), Password), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(Json, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.Equal("Tester", user.Name);
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.SingleAsync(x => x.Id == user.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()
            .VerifyHashedPassword(stored, stored.PasswordHash, Password));
        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Password, body);
        Assert.DoesNotContain(stored.PasswordHash, body);
        Assert.DoesNotContain(Password, string.Join('\n', fixture.App.Logs.Messages));
        Assert.DoesNotContain(stored.PasswordHash, string.Join('\n', fixture.App.Logs.Messages));
    }

    [Fact]
    public async Task Concurrent_registration_keeps_one_normalized_email()
    {
        var email = Email();
        var replies = await Task.WhenAll(
            fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("First", email, Password), cancellationToken: TestContext.Current.CancellationToken),
            fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Second", email.ToUpperInvariant(), Password), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
        using var scope = fixture.App.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(x => x.Email == email, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Login_accepts_valid_credentials_and_returns_the_same_error_for_invalid_credentials()
    {
        var email = Email();
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Tester", email, Password), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var wrong = await fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword!"), cancellationToken: TestContext.Current.CancellationToken);
        var unknown = await fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Email(), Password), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var wrongProblem = await wrong.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        var unknownProblem = await unknown.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(wrongProblem?.Title, unknownProblem?.Title);
        var valid = await fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email.ToUpperInvariant(), Password), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var session = await valid.Content.ReadFromJsonAsync<LoginResponse>(Json, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.DoesNotContain(session.AccessToken, string.Join('\n', fixture.App.Logs.Messages));
        using var authorized = fixture.App.CreateClient();
        authorized.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await authorized.GetAsync("/api/products", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/products")]
    [InlineData("POST", "/api/products")]
    [InlineData("GET", "/api/products/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/products/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/products/00000000-0000-0000-0000-000000000001")]
    public async Task All_product_endpoints_require_authentication(string method, string path) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

    [Fact]
    public async Task Product_validation_and_crud_preserve_timestamps_and_inactive_products()
    {
        using var client = await AuthorizedClient();
        var category = await (await client.PostAsJsonAsync("/api/categories", new CategoryRequest("Test", "", true), TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<CategoryResponse>(TestContext.Current.CancellationToken);
        var input = new ProductRequest("Notebook", "Test", 9999999999.99m, true, category!.Id);
        var before = await client.GetFromJsonAsync<List<ProductResponse>>("/api/products", cancellationToken: TestContext.Current.CancellationToken);
        foreach (var invalid in new[] { input with { Name = " " }, input with { Price = -1 }, input with { Price = 1.234m }, input with { Price = 10000000000m } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", invalid, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(before?.Count, (await client.GetFromJsonAsync<List<ProductResponse>>("/api/products", cancellationToken: TestContext.Current.CancellationToken))?.Count);
        var created = await client.PostAsJsonAsync("/api/products", input, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(product);
        Assert.Null(product.UpdatedAt);
        Assert.EndsWith(product.Id.ToString(), created.Headers.Location?.ToString());
        var update = await client.PutAsJsonAsync($"/api/products/{product.Id}", input with { Name = "Updated", IsActive = false, Price = 24.50m }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal(product.CreatedAt, saved.CreatedAt);
        Assert.True(saved.UpdatedAt >= saved.CreatedAt);
        Assert.Equal(24.50m, saved.Price);
        Assert.False(saved.IsActive);
        Assert.Contains((await client.GetFromJsonAsync<List<ProductResponse>>("/api/products", cancellationToken: TestContext.Current.CancellationToken))!, x => x.Id == product.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{product.Id}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{product.Id}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/products/{product.Id}", input, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_delete_and_stale_update_return_not_found_instead_of_server_error()
    {
        using var client = await AuthorizedClient();
        var category = await (await client.PostAsJsonAsync("/api/categories", new CategoryRequest("Race", "", true), TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<CategoryResponse>(TestContext.Current.CancellationToken);
        var input = new ProductRequest("Race", "", 1m, true, category!.Id);
        var result = await client.PostAsJsonAsync("/api/products", input, cancellationToken: TestContext.Current.CancellationToken);
        var product = await result.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(product);
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull(await db.Products.FindAsync([product.Id], TestContext.Current.CancellationToken)); // Simulate a row read before a concurrent deletion.
        var deletes = await Task.WhenAll(client.DeleteAsync($"/api/products/{product.Id}", cancellationToken: TestContext.Current.CancellationToken), client.DeleteAsync($"/api/products/{product.Id}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(deletes, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(deletes, x => x.StatusCode == HttpStatusCode.NotFound);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ProductService>().Update(product.Id, input, TestContext.Current.CancellationToken));
    }
}
