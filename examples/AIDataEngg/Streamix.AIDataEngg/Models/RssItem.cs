namespace Streamix.AIDataEngg.Models;

public class RssItem
{
    public int Id { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
    public string FeedName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Link { get; set; }
    public DateTimeOffset Published { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public bool Processed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
