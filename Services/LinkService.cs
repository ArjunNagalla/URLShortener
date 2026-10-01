using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using ShortUrl.Api.Domain;
using ShortUrl.Api.Infrastructure;
using ShortUrl.Api.Models;

namespace ShortUrl.Api.Services;

public class LinkService : ILinkService
{
    private const int MaxCodeGenerationAttempts = 5;
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

    public async Task<(LinkResponse? Response, string? Error, int StatusCode)> CreateLinkAsync(
        CreateLinkRequest request,
        string baseUrl,
        CancellationToken cancellationToken = default)
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

        }

        for (var attempt = 0; attempt < MaxCodeGenerationAttempts; attempt++)
        {
            var code = customAlias ?? _codeGenerator.GenerateCode();
            var codeExists = await _db.Links.AnyAsync(
                link => link.Code == code || link.CustomAlias == code,
                cancellationToken);
            if (codeExists)
            {
                if (customAlias is not null)
                {
                    return (null, "Custom alias is already in use.", 409);
                }

                continue;
            }

            var link = new Link
            {
                Code = code,
                DestinationUrl = request.Url,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = request.ExpiresAt,
                CustomAlias = customAlias
            };

            _db.Links.Add(link);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                var response = new LinkResponse(
                    link.Code,
                    $"{baseUrl.TrimEnd('/')}/{link.Code}",
                    link.DestinationUrl,
                    link.CreatedAtUtc,
                    link.ExpiresAtUtc,
                    link.CustomAlias);

                return (response, null, 201);
            }
            catch (DbUpdateException)
            {
                _db.Entry(link).State = EntityState.Detached;
                var codeWasClaimed = await _db.Links.AnyAsync(
                    existing => existing.Code == code || existing.CustomAlias == code,
                    cancellationToken);
                if (!codeWasClaimed)
                {
                    throw;
                }

                if (customAlias is not null)
                {
                    return (null, "Custom alias is already in use.", 409);
                }
            }
        }

        return (null, "Failed to generate a unique short code. Please try again.", 503);
    }

    public async Task<(string? DestinationUrl, string? ErrorStatus)> ResolveLinkAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var link = await _db.Links.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code, cancellationToken);
        if (link == null)
        {
            return (null, "404");
        }

        var accessedAtUtc = DateTime.UtcNow;
        if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value <= accessedAtUtc)
        {
            return (null, "410");
        }

        var updatedRows = await _db.Links
            .Where(existing => existing.Id == link.Id &&
                (!existing.ExpiresAtUtc.HasValue || existing.ExpiresAtUtc > accessedAtUtc))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(existing => existing.TotalClicks, existing => existing.TotalClicks + 1)
                .SetProperty(existing => existing.LastAccessedAtUtc, accessedAtUtc), cancellationToken);

        if (updatedRows == 0)
        {
            var stillExists = await _db.Links.AnyAsync(existing => existing.Id == link.Id, cancellationToken);
            return (null, stillExists ? "410" : "404");
        }

        return (link.DestinationUrl, null);
    }

    public async Task<LinkStatsResponse?> GetStatsAsync(string code, CancellationToken cancellationToken = default)
    {
        var link = await _db.Links.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code, cancellationToken);
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
