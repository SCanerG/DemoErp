using Demo.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Testcontainers.PostgreSql;

namespace Demo.Api.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("catalog_tests").WithUsername("catalog")
        .WithPassword("test-only-password").Build();
    public TestApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public async ValueTask InitializeAsync()
    {
        await database.StartAsync();
        App = new TestApplication(database.GetConnectionString());
        Client = App.CreateClient(); // Real startup applies the committed migrations.
    }
    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
        await database.DisposeAsync();
    }
}

public sealed class TestApplication(string connectionString) : WebApplicationFactory<Program>
{
    public CapturedLogs Logs { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Jwt:Secret"] = "test-only-signing-key-with-at-least-32-bytes",
            ["Jwt:Issuer"] = "catalog-tests", ["Jwt:Audience"] = "catalog-tests",
            ["Cors:Origins:0"] = "http://localhost:3000"
        }));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}

public sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
    public void Dispose() { }
    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
