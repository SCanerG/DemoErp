using System.Net;
using System.Net.Http.Json;

namespace Demo.Api.Tests;

public sealed class HealthTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Healthy_endpoints_keep_a_minimal_public_contract(string path)
    {
        var response = await fixture.Client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(TestContext.Current.CancellationToken);
        Assert.Single(body!);
        Assert.Equal("healthy", body!["status"]);
    }

    [Fact]
    public async Task Database_outage_does_not_fail_liveness_and_readiness_is_bounded()
    {
        await fixture.StopDatabase();
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var response = await fixture.Client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        }
        finally { await fixture.StartDatabase(); }
    }
}
