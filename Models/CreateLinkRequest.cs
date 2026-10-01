namespace ShortUrl.Api.Models;

public record CreateLinkRequest(
    string Url,
    DateTime? ExpiresAt = null,
    string? CustomAlias = null
);
