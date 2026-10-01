using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ShortUrl.Api.Infrastructure;
using ShortUrl.Api.Middleware;
using ShortUrl.Api.Models;
using ShortUrl.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database");

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));
app.MapGet("/health/ready", async (ApplicationDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect ? Results.Ok(new { status = "Ready", database = "Connected" })
                      : Results.StatusCode(503);
});

app.MapPost("/api/links", async (CreateLinkRequest request, ILinkService linkService, IConfiguration config, HttpContext httpContext) =>
{
    var baseUrl = config["App:BaseUrl"] ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    var (response, error, statusCode) = await linkService.CreateLinkAsync(request, baseUrl);

    if (error != null)
    {
        return Results.Json(new { error }, statusCode: statusCode);
    }

    return Results.Created($"/api/links/{response!.Code}/stats", response);
});

app.MapGet("/api/links/{code}/stats", async (string code, ILinkService linkService) =>
{
    var stats = await linkService.GetStatsAsync(code);
    return stats != null ? Results.Ok(stats) : Results.NotFound(new { error = "Short code not found." });
});

app.MapGet("/{code}", async (string code, ILinkService linkService, HttpContext context) =>
{
    var (destinationUrl, errorStatus) = await linkService.ResolveLinkAsync(code);

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
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.Run();

public partial class Program { }
