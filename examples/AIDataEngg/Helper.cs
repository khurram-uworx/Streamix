using AIDataEngg.Data;
using AIDataEngg.Models;
using AIDataEngg.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Streamix;

namespace AIDataEngg;

sealed record EmbeddedRssItem(
    RssItem Item,
    ReadOnlyMemory<float> Embedding,
    bool EmbeddingSucceeded);

static class Helper
{
    // Throws InvalidOperationException carrying ex.Data["HallucinatedSignal"] when the
    // LLM returns a signal not in validSignals; the surrounding retry/OnErrorResume
    // translates that into a final "General" + IsNoise=true after 3 attempts.
    public static async Task<ClassificationResult> ClassifyAndValidateAsync(
        IChatClient client, RssItem item, string systemPrompt,
        HashSet<string> validSignals, CancellationToken ct)
    {
        var r = await RssClassifier.ClassifyAsync(client, item, systemPrompt, ct);
        if (!validSignals.Contains(r.Signal))
        {
            var ex = new InvalidOperationException($"Invalid signal '{r.Signal}' from model");
            ex.Data["HallucinatedSignal"] = r.Signal;
            throw ex;
        }
        return r;
    }

    // If the local SQLite db exists but predates the Embedding column, drop it so
    // EnsureCreatedAsync rebuilds with the current schema. A cleaner alternative
    // would be EF migrations; for an example project, drop-and-recreate is fine.
    public static async Task EnsureSchemaAsync()
    {
        await using var db = new RssDbContext();
        if (await db.Database.CanConnectAsync())
        {
            try
            {
                _ = await db.Classifications.Select(c => c.Embedding).Take(1).ToListAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"[Schema] Detected legacy schema ({ex.GetType().Name}); rebuilding database...");
                await db.Database.EnsureDeletedAsync();
            }
        }
        await db.Database.EnsureCreatedAsync();
    }

    public static async Task<int> RestoreVectorStateAsync(
        VectorStoreCollection<int, VectorIndexEntry> collection,
        CategoryCentroidTracker centroids)
    {
        await using var db = new RssDbContext();
        // Note: SQLite's EF provider can't translate byte[].Length to SQL, so we
        // only filter by non-null on the server. ClassifiedRssItem.Embedding is
        // either null (LLM fallback embed-fail path) or a fully-formed byte[]
        // (PersistAndIndexAsync invariant), never an empty array.
        var prior = await db.Classifications
            .Where(c => c.Embedding != null)
            .Include(c => c.RssItem)
            .OrderBy(c => c.Id)
            .ToListAsync();

        if (prior.Count == 0) return 0;

        var entries = new List<VectorIndexEntry>(prior.Count);
        foreach (var c in prior)
        {
            if (c.Embedding is null || c.Embedding.Length == 0) continue;
            var emb = EmbeddingSerializer.FromBytes(c.Embedding!);
            centroids.AddOrUpdate(c.Signal, emb);
            entries.Add(new VectorIndexEntry
            {
                Id = c.Id,
                RssItemId = c.RssItemId,
                Signal = c.Signal,
                Title = c.RssItem.Title,
                Summary = c.RssItem.Summary,
                Embedding = emb,
            });
        }
        await collection.UpsertAsync(entries);
        return entries.Count;
    }

    public static async Task PersistAndIndexAsync(
        RssItem item,
        ReadOnlyMemory<float> embedding,
        ClassificationDecision decision,
        int attemptCount,
        VectorStoreCollection<int, VectorIndexEntry> collection,
        CategoryCentroidTracker centroids,
        CancellationToken ct)
    {
        await using var db = new RssDbContext();
        db.RssItems.Attach(item);
        item.Processed = true;

        var classified = new ClassifiedRssItem
        {
            RssItemId = item.Id,
            Signal = decision.Result.Signal,
            Reasoning = decision.Result.Reasoning,
            IsNoise = decision.Result.IsNoise,
            AttemptCount = attemptCount,
            HallucinatedSignal = decision.Result.HallucinatedSignal,
            Embedding = embedding.IsEmpty ? null : EmbeddingSerializer.ToBytes(embedding),
        };
        db.Classifications.Add(classified);
        await db.SaveChangesAsync(ct);

        if (embedding.IsEmpty) return;

        await collection.UpsertAsync(new VectorIndexEntry
        {
            Id = classified.Id,
            RssItemId = item.Id,
            Signal = decision.Result.Signal,
            Title = item.Title,
            Summary = item.Summary,
            Embedding = embedding,
        }, cancellationToken: ct);

        centroids.AddOrUpdate(decision.Result.Signal, embedding);
    }

}
