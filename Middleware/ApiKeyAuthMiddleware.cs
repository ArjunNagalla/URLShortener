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

        if (!path.StartsWith("/api/links") || context.Request.Method == "GET" && !path.Contains("/stats"))
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

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var extractedApiKey) ||
            !string.Equals(extractedApiKey, expectedApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: Invalid or missing X-API-Key header.");
            return;
        }

        await _next(context);
    }
}
