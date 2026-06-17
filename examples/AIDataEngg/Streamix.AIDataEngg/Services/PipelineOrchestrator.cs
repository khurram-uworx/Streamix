using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;

namespace Streamix.AIDataEngg.Services;

public class PipelineOrchestrator(
    RssDbContext db,
    ConfigLoader configLoader,
    EmbeddingService embeddingService,
    IChatClient chatClient,
    CategoryCentroidTracker centroids)
{
    public async Task<PipelineCompleted> RunAsync(
        PipelineConfig config,
        IProgress<PipelineEvent> progress,
        CancellationToken ct = default)
    {
        progress.Report(new PipelineStarted(config.FeedSources.Count));

        var signalsText = string.Join("\n", config.Signals.Select(s => $"- {s}"));
        var validSignals = config.Signals.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var systemPrompt = config.PromptTemplate
            .Replace("{goalText}", config.Goal)
            .Replace("{signalsText}", signalsText);

        var collection = await VectorStoreProvider.GetOrCreateCollectionAsync(
            config.VectorCollectionName, config.EmbeddingDimension, ct);

        progress.Report(new PipelineProgress("Restore", 0, 1, "Restoring vector store from prior classifications"));
        var restoredCount = await RestoreVectorStateAsync(db, collection, centroids, ct);
        progress.Report(new PipelineProgress("Restore", 1, 1, $"Restored {restoredCount} embeddings"));

        var llmFallback = CreateLlmFallback(chatClient, systemPrompt, validSignals);
        var hybridClassifier = new VectorClassifier(
            collection, llmFallback, validSignals, centroids,
            bootstrapThreshold: config.BootstrapThreshold,
            topK: config.TopK,
            minNeighbors: config.MinNeighbors,
            minNeighborAgreement: config.MinNeighborAgreement,
            minAvgSimilarity: config.MinAvgSimilarity,
            minMargin: config.MinMargin);

        // Stage 1 & 2: Fetch + dedup
        progress.Report(new PipelineProgress("Fetch", 0, config.FeedSources.Count, "Fetching feeds"));
        var fetchCount = 0;
        foreach (var source in config.FeedSources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var savedCount = 0;
                await foreach (var item in RssFetcher.FetchFeedAsync(source.Url, source.Name, ct))
                {
                    if (await db.RssItems.AnyAsync(r => r.ContentHash == item.ContentHash, ct))
                        continue;
                    db.RssItems.Add(item);
                    savedCount++;
                }
                await db.SaveChangesAsync(ct);
                fetchCount++;
                progress.Report(new PipelineProgress("Fetch", fetchCount, config.FeedSources.Count, $"Fetched {source.Name}"));
            }
            catch (Exception ex)
            {
                progress.Report(new PipelineFailed("Fetch", ex.Message));
            }
        }

        // Stage 3: unprocessed items
        var unprocessedIds = await db.RssItems
            .Where(r => !r.Processed)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (unprocessedIds.Count == 0)
        {
            progress.Report(new PipelineCompleted(0, 0, 0, 0));
            return new PipelineCompleted(0, 0, 0, 0);
        }

        var totalUnprocessed = unprocessedIds.Count;
        progress.Report(new PipelineProgress("Classify", 0, totalUnprocessed,
            $"Found {totalUnprocessed} items to classify"));

        // Stage 4 & 5: Embed → classify → persist
        var processedSoFar = 0;
        var autoCount = 0;
        var llmCount = 0;
        var embedFailures = 0;
        var initialClassifiedCount = await db.Classifications.LongCountAsync(ct);

        foreach (var rssItemId in unprocessedIds)
        {
            ct.ThrowIfCancellationRequested();

            var item = await db.RssItems.FindAsync([rssItemId], ct);
            if (item is null) continue;

            // Embed
            ReadOnlyMemory<float> embedding;
            bool embedSucceeded;
            try
            {
                embedding = await embeddingService.GenerateAsync(item, ct);
                embedSucceeded = true;
            }
            catch (Exception ex)
            {
                embedFailures++;
                progress.Report(new PipelineFailed("Embed", ex.Message, item.Id));
                embedding = ReadOnlyMemory<float>.Empty;
                embedSucceeded = false;
            }

            // Classify
            ClassificationDecision decision;
            var attemptCount = 0;
            var currentCount = (int)(initialClassifiedCount + processedSoFar);

            try
            {
                if (!embedSucceeded)
                {
                    var llmResult = await ClassifyAndValidateAsync(chatClient, item, systemPrompt, validSignals, ct);
                    decision = new ClassificationDecision(llmResult, ClassificationSource.LlmEmbeddingFailed, NeighborStats.Empty);
                }
                else
                {
                    decision = await hybridClassifier.ClassifyAsync(item, embedding, currentCount, ct);
                }

                if (decision.WasAutoLabelled) autoCount++;
                else llmCount++;
            }
            catch (Exception ex) when (ex.Data.Contains("HallucinatedSignal"))
            {
                var hallucinated = ex.Data["HallucinatedSignal"] as string;
                decision = new ClassificationDecision(
                    new ClassificationResult("General",
                        $"All attempts failed. Last invalid signal: '{hallucinated}'",
                        IsNoise: true, HallucinatedSignal: hallucinated),
                    ClassificationSource.Failed, NeighborStats.Empty);
                llmCount++;
            }

            // Persist
            await PersistAndIndexAsync(db, item, embedding, decision, attemptCount, collection, centroids, ct);

            processedSoFar++;
            progress.Report(new PipelineItemProcessed(
                item.Id, item.Title, decision.Result.Signal, decision.Result.IsNoise,
                decision.Result.Reasoning, decision.Result.HallucinatedSignal));

            progress.Report(new PipelineProgress("Classify", processedSoFar, totalUnprocessed,
                $"[{(decision.WasAutoLabelled ? "auto" : "llm")}] {item.Title} -> {decision.Result.Signal}"));
        }

        var completed = new PipelineCompleted(
            TotalItems: processedSoFar,
            SignalCount: processedSoFar - embedFailures,
            NoiseCount: 0,
            FailedCount: embedFailures);

        progress.Report(completed);
        return completed;
    }

    static async Task<ClassificationResult> ClassifyAndValidateAsync(
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

    static VectorClassifier.LlmFallbackDelegate CreateLlmFallback(
        IChatClient chatClient, string systemPrompt, HashSet<string> validSignals)
    {
        return (item, ct) => ClassifyAndValidateAsync(chatClient, item, systemPrompt, validSignals, ct);
    }

    static async Task<int> RestoreVectorStateAsync(
        RssDbContext db,
        VectorStoreCollection<int, VectorIndexEntry> collection,
        CategoryCentroidTracker centroids,
        CancellationToken ct)
    {
        var prior = await db.Classifications
            .Where(c => c.Embedding != null)
            .Include(c => c.RssItem)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        if (prior.Count == 0) return 0;

        var entries = new List<VectorIndexEntry>(prior.Count);
        foreach (var c in prior)
        {
            if (c.Embedding is null || c.Embedding.Length == 0) continue;
            var emb = EmbeddingSerializer.FromBytes(c.Embedding);
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
        await collection.UpsertAsync(entries, ct);
        return entries.Count;
    }

    static async Task PersistAndIndexAsync(
        RssDbContext db,
        RssItem item,
        ReadOnlyMemory<float> embedding,
        ClassificationDecision decision,
        int attemptCount,
        VectorStoreCollection<int, VectorIndexEntry> collection,
        CategoryCentroidTracker centroids,
        CancellationToken ct)
    {
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
