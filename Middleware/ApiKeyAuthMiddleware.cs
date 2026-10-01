using System.Security.Cryptography;
using System.Text;

namespace ShortUrl.Api.Middleware;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeader = "X-API-Key";

    public ApiKeyAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isLinksPath = path.Equals("/api/links", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/links/", StringComparison.OrdinalIgnoreCase);

        if (!isLinksPath || context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains("/stats", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var expectedApiKey = config["Security:ApiKey"];
        if (string.IsNullOrEmpty(expectedApiKey))
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("API Key configuration missing on server.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: Invalid or missing X-API-Key header.");
            return;
        }

        var expectedKeyBytes = Encoding.UTF8.GetBytes(expectedApiKey);
        var providedKeyBytes = Encoding.UTF8.GetBytes(extractedApiKey.ToString());
        if (expectedKeyBytes.Length != providedKeyBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedKeyBytes, providedKeyBytes))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: Invalid or missing X-API-Key header.");
            return;
        }

        await _next(context);
    }
}
