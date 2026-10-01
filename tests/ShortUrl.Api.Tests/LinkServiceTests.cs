using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShortUrl.Api.Domain;
using ShortUrl.Api.Infrastructure;
using ShortUrl.Api.Middleware;
using ShortUrl.Api.Models;
using ShortUrl.Api.Services;
using Xunit;

namespace ShortUrl.Api.Tests;

public class LinkServiceTests
{
    [Fact]
    public void GenerateCode_ReturnsShortBase62Code()
    {
        var generator = new Base62CodeGenerator();

        var code = generator.GenerateCode();

        Assert.Equal(6, code.Length);
        Assert.All(code, ch => Assert.Contains(ch, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"));
    }

    [Fact]
    public async Task CreateLinkAsync_WithCustomAlias_ReturnsExpectedShortUrl()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();

            var service = new LinkService(db, new Base62CodeGenerator());

            var result = await service.CreateLinkAsync(
                new CreateLinkRequest("https://example.com", CustomAlias: "qa1234"),
                "http://localhost:5000");

            Assert.Null(result.Error);
            Assert.Equal(201, result.StatusCode);
            Assert.NotNull(result.Response);
            Assert.Equal("qa1234", result.Response!.Code);
            Assert.Equal("http://localhost:5000/qa1234", result.Response.ShortUrl);
        }
    }

    [Theory]
    [InlineData("/api/links", "POST")]
    [InlineData("/API/LINKS", "POST")]
    [InlineData("/api/links/qa1234/stats", "GET")]
    [InlineData("/API/LINKS/qa1234/STATS", "GET")]
    public async Task ApiKeyMiddleware_ProtectsManagementRoutesRegardlessOfCasing(string path, string method)
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:ApiKey"] = "test-api-key" })
            .Build();

        await middleware.InvokeAsync(context, configuration);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task ApiKeyMiddleware_AllowsPublicRedirectRoutes()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/qa1234";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context, new ConfigurationBuilder().Build());

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task CreateLinkAsync_UsesCodeGeneratedOnFinalRetry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Links.Add(new Link { Code = "taken1", DestinationUrl = "https://existing.example" });
        await db.SaveChangesAsync();

        var generator = new SequenceCodeGenerator("taken1", "taken1", "taken1", "taken1", "fresh1");
        var service = new LinkService(db, generator);

        var result = await service.CreateLinkAsync(
            new CreateLinkRequest("https://example.com"),
            "http://localhost:5000");

        Assert.Null(result.Error);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("fresh1", result.Response!.Code);
        Assert.Equal(5, generator.Calls);
    }

    [Fact]
    public async Task ResolveLinkAsync_IncrementsClickCountForEverySuccessfulResolution()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Links.Add(new Link { Code = "click1", DestinationUrl = "https://example.com" });
        await db.SaveChangesAsync();
        var service = new LinkService(db, new Base62CodeGenerator());

        for (var click = 0; click < 3; click++)
        {
            var resolved = await service.ResolveLinkAsync("click1");
            Assert.Equal("https://example.com", resolved.DestinationUrl);
            Assert.Null(resolved.ErrorStatus);
        }

        var stats = await service.GetStatsAsync("click1");

        Assert.NotNull(stats);
        Assert.Equal(3, stats.TotalClicks);
        Assert.NotNull(stats.LastAccessedAt);
    }

    [Fact]
    public async Task ResolveLinkAsync_RejectsExpiredLinkWithoutCountingClick()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Links.Add(new Link
        {
            Code = "expired1",
            DestinationUrl = "https://example.com",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        var service = new LinkService(db, new Base62CodeGenerator());

        var resolved = await service.ResolveLinkAsync("expired1");
        var stats = await service.GetStatsAsync("expired1");

        Assert.Null(resolved.DestinationUrl);
        Assert.Equal("410", resolved.ErrorStatus);
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalClicks);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a URL")]
    public async Task CreateLinkAsync_RejectsNonHttpDestination(string url)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new LinkService(db, new Base62CodeGenerator());

        var result = await service.CreateLinkAsync(new CreateLinkRequest(url), "http://localhost:5000");

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("HTTP or HTTPS", result.Error);
    }

    private sealed class SequenceCodeGenerator(params string[] codes) : ICodeGenerator
    {
        private int _index;

        public int Calls => _index;

        public string GenerateCode(int length = 6)
        {
            var code = codes[Math.Min(_index, codes.Length - 1)];
            _index++;
            return code;
        }
    }
}
