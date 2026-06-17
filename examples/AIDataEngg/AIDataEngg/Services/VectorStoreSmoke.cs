using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Services;

// Standalone smoke test for the vector store wiring (Plan2 Task 1 acceptance).
// Run with: dotnet run --project examples/AIDataEngg -- --smoke
internal static class VectorStoreSmoke
{
    public static async Task<int> RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("[Smoke] Creating in-memory vector collection...");
        var collection = await VectorStoreProvider.GetOrCreateCollectionAsync("smoke-vectors", EmbeddingDefaults.Dimensions, ct);

        var anchor = MakeUnitVector(seed: 1);
        var nearAnchor = Perturb(anchor, magnitude: 0.05f, seed: 2);
        var farFromAnchor = MakeUnitVector(seed: 99);

        var records = new[]
        {
            new VectorIndexEntry { Id = 1, RssItemId = 101, Signal = "AI/ML",      Title = "Anchor item",      Embedding = anchor },
            new VectorIndexEntry { Id = 2, RssItemId = 102, Signal = "AI/ML",      Title = "Near-anchor item", Embedding = nearAnchor },
            new VectorIndexEntry { Id = 3, RssItemId = 103, Signal = "Security",   Title = "Far item",         Embedding = farFromAnchor },
        };

        Console.WriteLine($"[Smoke] Upserting {records.Length} records...");
        await collection.UpsertAsync(records, ct);

        var fetched = await collection.GetAsync(1, cancellationToken: ct);
        if (fetched is null || fetched.Title != "Anchor item")
        {
            Console.Error.WriteLine($"[Smoke] FAIL: GetAsync(1) returned '{fetched?.Title ?? "null"}'");
            return 1;
        }
        Console.WriteLine($"[Smoke] GetAsync(1) -> '{fetched.Title}' (Signal={fetched.Signal})");

        Console.WriteLine("[Smoke] Searching for top 2 nearest to 'anchor'...");
        var hits = new List<(int Id, string Title, double Score)>();
        await foreach (var hit in collection.SearchAsync(anchor, top: 2, cancellationToken: ct))
        {
            hits.Add((hit.Record.Id, hit.Record.Title, hit.Score ?? 0.0));
            Console.WriteLine($"  -> Id={hit.Record.Id} Title='{hit.Record.Title}' Score={hit.Score:F4}");
        }

        if (hits.Count < 2 || hits[0].Id != 1 || hits[1].Id != 2)
        {
            Console.Error.WriteLine("[Smoke] FAIL: expected the anchor (Id=1) and near-anchor (Id=2) as top 2 hits.");
            return 1;
        }

        Console.WriteLine("[Smoke] PASS");
        return 0;
    }

    private static ReadOnlyMemory<float> MakeUnitVector(int seed)
    {
        var rng = new Random(seed);
        var v = new float[EmbeddingDefaults.Dimensions];
        double sumSq = 0.0;
        for (var i = 0; i < EmbeddingDefaults.Dimensions; i++)
        {
            v[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            sumSq += v[i] * v[i];
        }
        var norm = (float)Math.Sqrt(sumSq);
        for (var i = 0; i < EmbeddingDefaults.Dimensions; i++) v[i] /= norm;
        return v;
    }

    private static ReadOnlyMemory<float> Perturb(ReadOnlyMemory<float> source, float magnitude, int seed)
    {
        var rng = new Random(seed);
        var src = source.Span;
        var v = new float[src.Length];
        double sumSq = 0.0;
        for (var i = 0; i < src.Length; i++)
        {
            v[i] = src[i] + (float)((rng.NextDouble() * 2.0 - 1.0) * magnitude);
            sumSq += v[i] * v[i];
        }
        var norm = (float)Math.Sqrt(sumSq);
        for (var i = 0; i < src.Length; i++) v[i] /= norm;
        return v;
    }
}
