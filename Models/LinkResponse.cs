namespace ShortUrl.Api.Models;

public record LinkResponse(
    string Code,
    string ShortUrl,
    string DestinationUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    string? CustomAlias
);
