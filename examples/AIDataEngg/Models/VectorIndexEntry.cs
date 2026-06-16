using Microsoft.Extensions.VectorData;

namespace AIDataEngg.Models;

static class EmbeddingDefaults
{
    public const string ModelName = "nomic-embed-text";

    // Keep this in sync with ModelName and AI_EMBEDDING_MODEL. Changing the
    // embedding dimension changes the vector schema; this example has no EF or
    // vector-store migrations, so existing local data may need to be deleted and
    // restored/reclassified after a model swap.
    public const int Dimensions = 768;
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

    [VectorStoreVector(EmbeddingDefaults.Dimensions, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
