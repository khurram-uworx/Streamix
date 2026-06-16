using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Streamix.AIDataEngg.Models;

namespace Streamix.AIDataEngg.Services;

public static class VectorStoreProvider
{
    public static async Task<VectorStoreCollection<int, VectorIndexEntry>> GetOrCreateCollectionAsync(
        string collectionName = "rss-vectors",
        CancellationToken ct = default)
    {
        var vectorStore = new InMemoryVectorStore();

        var collection = vectorStore.GetCollection<int, VectorIndexEntry>(collectionName);

        await collection.EnsureCollectionExistsAsync(ct);

        return collection;
    }
}
