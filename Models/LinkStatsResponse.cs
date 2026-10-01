namespace ShortUrl.Api.Models;

public record LinkStatsResponse(
    string Code,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    long TotalClicks,
    DateTime? LastAccessedAt
);
