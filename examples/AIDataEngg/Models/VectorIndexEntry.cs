using Microsoft.Extensions.VectorData;

namespace AIDataEngg.Models;

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

    // Dimension matches the default embedding model (nomic-embed-text = 768).
    // If you swap models, update both this attribute and AI_EMBEDDING_MODEL.
    [VectorStoreVector(768, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
