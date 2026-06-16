namespace Streamix.AIDataEngg.Models;

public class ClassifiedRssItem
{
    public int Id { get; set; }
    public int RssItemId { get; set; }
    public RssItem RssItem { get; set; } = null!;
    public string Signal { get; set; } = string.Empty;
    public string? Reasoning { get; set; }
    public bool IsNoise { get; set; }
    public int AttemptCount { get; set; }
    public string? HallucinatedSignal { get; set; }
    public DateTimeOffset ClassifiedAt { get; set; } = DateTimeOffset.UtcNow;

    public byte[]? Embedding { get; set; }
}
