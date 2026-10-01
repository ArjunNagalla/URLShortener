using ShortUrl.Api.Models;

namespace ShortUrl.Api.Services;

public interface ILinkService
{
    Task<(LinkResponse? Response, string? Error, int StatusCode)> CreateLinkAsync(
        CreateLinkRequest request,
        string baseUrl,
        CancellationToken cancellationToken = default);
    Task<(string? DestinationUrl, string? ErrorStatus)> ResolveLinkAsync(
        string code,
        CancellationToken cancellationToken = default);
    Task<LinkStatsResponse?> GetStatsAsync(string code, CancellationToken cancellationToken = default);
}
