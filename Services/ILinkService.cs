using ShortUrl.Api.Models;

namespace ShortUrl.Api.Services;

public interface ILinkService
{
    Task<(LinkResponse? Response, string? Error, int StatusCode)> CreateLinkAsync(CreateLinkRequest request, string baseUrl);
    Task<(string? DestinationUrl, string? ErrorStatus)> ResolveLinkAsync(string code);
    Task<LinkStatsResponse?> GetStatsAsync(string code);
}
