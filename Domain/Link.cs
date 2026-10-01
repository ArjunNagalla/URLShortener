namespace ShortUrl.Api.Domain;

public class Link
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string DestinationUrl { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public long TotalClicks { get; set; } = 0;
    public DateTime? LastAccessedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? CustomAlias { get; set; }
}
