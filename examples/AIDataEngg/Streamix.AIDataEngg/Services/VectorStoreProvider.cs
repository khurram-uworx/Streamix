using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Streamix.AIDataEngg.Models;

namespace Streamix.AIDataEngg.Services;

public static class VectorStoreProvider
{
    public static async Task<VectorStoreCollection<int, VectorIndexEntry>> GetOrCreateCollectionAsync(
        string collectionName = "rss-vectors",
        int dimensions = 768,
        CancellationToken ct = default)
    {
        var vectorStore = new InMemoryVectorStore();

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("Id", typeof(int)),
                new VectorStoreDataProperty("RssItemId", typeof(int)),
                new VectorStoreDataProperty("Signal", typeof(string)),
                new VectorStoreDataProperty("Title", typeof(string)),
                new VectorStoreDataProperty("Summary", typeof(string)),
                new VectorStoreVectorProperty("Embedding", typeof(ReadOnlyMemory<float>), dimensions)
                {
                    DistanceFunction = DistanceFunction.CosineSimilarity
                },
            ]
        };

        var collection = vectorStore.GetCollection<int, VectorIndexEntry>(collectionName, definition);

        await collection.EnsureCollectionExistsAsync(ct);

        return collection;
    }
}
