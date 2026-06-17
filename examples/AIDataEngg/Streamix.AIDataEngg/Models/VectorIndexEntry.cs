using Microsoft.Extensions.VectorData;

namespace Streamix.AIDataEngg.Models;

public static class EmbeddingDefaults
{
    public const string ModelName = "nomic-embed-text";

    public static int Dimensions = 768;
}

public class VectorIndexEntry
{
    [VectorStoreKey]
    public int Id { get; set; }

    [VectorStoreData]
    public int RssItemId { get; set; }

    [VectorStoreData]
    public string Signal { get; set; } = string.Empty;

    [VectorStoreData]
    public string Title { get; set; } = string.Empty;

    [VectorStoreData]
    public string? Summary { get; set; }

    public ReadOnlyMemory<float> Embedding { get; set; }
}
