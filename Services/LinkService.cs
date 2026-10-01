using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Api.Domain;
using ShortUrl.Api.Infrastructure;
using ShortUrl.Api.Models;

namespace ShortUrl.Api.Services;

public class LinkService : ILinkService
{
    private readonly ApplicationDbContext _db;
    private readonly ICodeGenerator _codeGenerator;
    private static readonly HashSet<string> ReservedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "health", "swagger", "docs", "favicon.ico"
    };

    public LinkService(ApplicationDbContext db, ICodeGenerator codeGenerator)
    {
        _db = db;
        _codeGenerator = codeGenerator;
    }

    public async Task<(LinkResponse? Response, string? Error, int StatusCode)> CreateLinkAsync(CreateLinkRequest request, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            return (null, "Invalid destination URL. Must be an absolute HTTP or HTTPS URL.", 400);
        }

        if (request.Url.Length > 2048)
        {
            return (null, "Destination URL exceeds maximum allowed length of 2048 characters.", 400);
        }

        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow)
        {
            return (null, "Expiration time must be strictly in the future.", 400);
        }

        string finalCode;
        string? customAlias = null;

        if (!string.IsNullOrWhiteSpace(request.CustomAlias))
        {
            customAlias = request.CustomAlias.Trim();
            if (customAlias.Length < 4 || customAlias.Length > 32 || !Regex.IsMatch(customAlias, "^[a-zA-Z0-9-]+$"))
            {
                return (null, "Custom alias must be 4-32 alphanumeric characters or hyphens.", 400);
            }

            if (ReservedRoutes.Contains(customAlias))
            {
                return (null, "Custom alias uses a reserved route name.", 400);
            }

            bool aliasExists = await _db.Links.AnyAsync(l => l.Code == customAlias || l.CustomAlias == customAlias);
            if (aliasExists)
            {
                return (null, "Custom alias is already in use.", 409);
            }

            finalCode = customAlias;
        }
        else
        {
            int retries = 0;
            do
            {
                finalCode = _codeGenerator.GenerateCode();
                retries++;
            } while (await _db.Links.AnyAsync(l => l.Code == finalCode) && retries < 5);

            if (retries >= 5)
            {
                return (null, "Failed to generate a unique short code. Please try again.", 503);
            }
        }

        var link = new Link
        {
            Code = finalCode,
            DestinationUrl = request.Url,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = request.ExpiresAt,
            CustomAlias = customAlias
        };

        _db.Links.Add(link);
        await _db.SaveChangesAsync();

        var response = new LinkResponse(
            link.Code,
            $"{baseUrl.TrimEnd('/')}/{link.Code}",
            link.DestinationUrl,
            link.CreatedAtUtc,
            link.ExpiresAtUtc,
            link.CustomAlias
        );

        return (response, null, 201);
    }

    public async Task<(string? DestinationUrl, string? ErrorStatus)> ResolveLinkAsync(string code)
    {
        var link = await _db.Links.FirstOrDefaultAsync(l => l.Code == code);
        if (link == null)
        {
            return (null, "404");
        }

        if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            return (null, "410");
        }

        link.TotalClicks += 1;
        link.LastAccessedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (link.DestinationUrl, null);
    }

    public async Task<LinkStatsResponse?> GetStatsAsync(string code)
    {
        var link = await _db.Links.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code);
        if (link == null) return null;

        return new LinkStatsResponse(
            link.Code,
            link.CreatedAtUtc,
            link.ExpiresAtUtc,
            link.TotalClicks,
            link.LastAccessedAtUtc
        );
    }
}
