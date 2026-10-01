using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ShortUrl.Api.Infrastructure;
using ShortUrl.Api.Middleware;
using ShortUrl.Api.Models;
using ShortUrl.Api.OpenApi;
using ShortUrl.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var configuredApiKey = builder.Configuration["Security:ApiKey"];
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(configuredApiKey) || configuredApiKey.Length < 32))
{
    throw new InvalidOperationException("Configure Security:ApiKey with at least 32 characters outside Development.");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (!string.IsNullOrWhiteSpace(connectionString) && connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
{
    var sqliteConnectionString = new SqliteConnectionStringBuilder(connectionString);
    var dbDirectory = Path.GetDirectoryName(sqliteConnectionString.DataSource);
    if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
    {
        Directory.CreateDirectory(dbDirectory);
    }
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (connectionString is not null && connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
        return;
    }

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<ICodeGenerator, Base62CodeGenerator>();
builder.Services.AddScoped<ILinkService, LinkService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ShortURL API", Version = "v1" });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "X-API-Key header required for management endpoints",
        In = ParameterLocation.Header,
        Name = "X-API-Key",
        Type = SecuritySchemeType.ApiKey
    });
    c.OperationFilter<ApiKeyOperationFilter>();
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database");

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }))
    .WithSummary("Check whether the API process is running")
    .WithDescription("Returns HTTP 200 when the API process is alive. This check does not access the database.")
    .WithName("GetLiveness")
    .WithTags("Health");
app.MapGet("/health/ready", async (ApplicationDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        return canConnect ? Results.Ok(new { status = "Ready", database = "Connected" })
                          : Results.StatusCode(503);
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(503);
    }
})
    .WithSummary("Check whether the API can serve requests")
    .WithDescription("Checks database connectivity. Returns HTTP 200 when the database is reachable, or HTTP 503 when it is unavailable.")
    .WithName("GetReadiness")
    .WithTags("Health");

app.MapPost("/api/links", async (
    CreateLinkRequest request,
    ILinkService linkService,
    IConfiguration config,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var baseUrl = config["App:BaseUrl"] ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    var (response, error, statusCode) = await linkService.CreateLinkAsync(request, baseUrl, cancellationToken);

    if (error != null)
    {
        return Results.Json(new { error }, statusCode: statusCode);
    }

    return Results.Created($"/api/links/{response!.Code}/stats", response);
})
    .WithSummary("Create a short link")
    .WithDescription("Creates a short link for an absolute HTTP or HTTPS URL. Requires the X-API-Key header. Optionally accepts a custom alias and UTC expiration time. Returns HTTP 201 with the short URL, or an error for invalid input, a duplicate alias, or code-allocation failure.")
    .WithName("CreateLink")
    .WithTags("Links")
    .Produces<LinkResponse>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status409Conflict)
    .Produces(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/links/{code}/stats", async (string code, ILinkService linkService, CancellationToken cancellationToken) =>
{
    var stats = await linkService.GetStatsAsync(code, cancellationToken);
    return stats != null ? Results.Ok(stats) : Results.NotFound(new { error = "Short code not found." });
})
    .WithSummary("Get link click statistics")
    .WithDescription("Returns the total number of successful, unexpired redirect resolutions and the last access time for a short code. Requires the X-API-Key header. Returns HTTP 404 when the code does not exist.")
    .WithName("GetLinkStats")
    .WithTags("Links")
    .Produces<LinkStatsResponse>()
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/{code}", async (string code, ILinkService linkService, HttpContext context, CancellationToken cancellationToken) =>
{
    var (destinationUrl, errorStatus) = await linkService.ResolveLinkAsync(code, cancellationToken);

    if (errorStatus == "410")
    {
        return Results.StatusCode(410);
    }
    if (errorStatus == "404" || destinationUrl == null)
    {
        return Results.NotFound();
    }

    context.Response.Headers.CacheControl = "no-store, max-age=0";
    return Results.Redirect(destinationUrl, permanent: false);
})
    .WithSummary("Redirect a short code to its destination")
    .WithDescription("Resolves a public short code and responds with HTTP 302 to its destination. Successful, unexpired resolutions increment click statistics. Returns HTTP 404 for an unknown code and HTTP 410 for an expired link.")
    .WithName("ResolveLink")
    .WithTags("Redirects")
    .Produces(StatusCodes.Status302Found)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status410Gone);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program { }
