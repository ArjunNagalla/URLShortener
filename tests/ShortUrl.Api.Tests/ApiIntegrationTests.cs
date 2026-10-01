using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShortUrl.Api.Infrastructure;
using ShortUrl.Api.Models;
using Xunit;

namespace ShortUrl.Api.Tests;

public class ApiIntegrationTests
{
    private const string ApiKey = "integration-test-api-key-32-chars-minimum";

    [Fact]
    public async Task CreateRedirectAndStatsFlow_EnforcesKeyAndTracksClick()
    {
        using var factory = new ShortUrlApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var request = new CreateLinkRequest("https://example.com/article", CustomAlias: "intg123");

        using var liveness = await client.GetAsync("/health/live");
        using var readiness = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var unauthorized = await client.PostAsJsonAsync("/api/links", request);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        using var created = await client.PostAsJsonAsync("/api/links", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = await created.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(link);
        Assert.Equal("http://short.test/intg123", link.ShortUrl);

        using var redirect = await client.GetAsync("/intg123");
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal(new Uri("https://example.com/article"), redirect.Headers.Location);

        using var statsResponse = await client.GetAsync("/api/links/intg123/stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        var stats = await statsResponse.Content.ReadFromJsonAsync<LinkStatsResponse>();
        Assert.NotNull(stats);
        Assert.Equal(1, stats.TotalClicks);
    }

    [Fact]
    public async Task Swagger_RequiresApiKeyOnlyForManagementRoutes()
    {
        using var factory = new ShortUrlApiFactory();
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = document.GetProperty("paths");
        var createOperation = paths.GetProperty("/api/links").GetProperty("post");
        var redirectOperation = paths.GetProperty("/{code}").GetProperty("get");
        var operations = new[]
        {
            paths.GetProperty("/health/live").GetProperty("get"),
            paths.GetProperty("/health/ready").GetProperty("get"),
            createOperation,
            paths.GetProperty("/api/links/{code}/stats").GetProperty("get"),
            redirectOperation
        };

        foreach (var operation in operations)
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
        }

        Assert.True(createOperation.TryGetProperty("security", out _));
        Assert.False(redirectOperation.TryGetProperty("security", out _));
        Assert.False(document.TryGetProperty("security", out _));
    }

    private sealed class ShortUrlApiFactory : WebApplicationFactory<global::Program>
    {
        private readonly SqliteConnection _keepAliveConnection;

        public ShortUrlApiFactory()
        {
            _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
            _keepAliveConnection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Security:ApiKey"] = ApiKey,
                    ["App:BaseUrl"] = "http://short.test"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.AddScoped(_ => new ApplicationDbContext(
                    new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_keepAliveConnection).Options));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _keepAliveConnection.Dispose();
            }
        }
    }
}
