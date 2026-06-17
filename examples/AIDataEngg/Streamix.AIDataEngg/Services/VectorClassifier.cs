using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Streamix.AIDataEngg.Models;

namespace Streamix.AIDataEngg.Services;

public sealed class VectorClassifier
{
    public delegate Task<ClassificationResult> LlmFallbackDelegate(RssItem item, CancellationToken ct);

    public const int DefaultBootstrapThreshold = 20;
    public const int DefaultTopK = 10;
    public const int DefaultMinNeighbors = 5;
    public const int DefaultMinNeighborAgreement = 5;
    public const float DefaultMinAvgSimilarity = 0.86f;
    public const float DefaultMinMargin = 0.10f;

    readonly VectorStoreCollection<int, VectorIndexEntry> collection;
    readonly LlmFallbackDelegate llmFallback;
    readonly HashSet<string> validSignals;
    readonly CategoryCentroidTracker? centroids;

    readonly int bootstrapThreshold;
    readonly int topK;
    readonly int minNeighbors;
    readonly int minNeighborAgreement;
    readonly float minAvgSimilarity;
    readonly float minMargin;

    public VectorClassifier(
        VectorStoreCollection<int, VectorIndexEntry> collection,
        LlmFallbackDelegate llmFallback,
        IEnumerable<string> validSignals,
        CategoryCentroidTracker? centroids = null,
        int bootstrapThreshold = DefaultBootstrapThreshold,
        int topK = DefaultTopK,
        int minNeighbors = DefaultMinNeighbors,
        int minNeighborAgreement = DefaultMinNeighborAgreement,
        float minAvgSimilarity = DefaultMinAvgSimilarity,
        float minMargin = DefaultMinMargin)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(llmFallback);
        ArgumentNullException.ThrowIfNull(validSignals);

        this.collection = collection;
        this.llmFallback = llmFallback;
        this.validSignals = new HashSet<string>(validSignals, StringComparer.OrdinalIgnoreCase);
        if (this.validSignals.Count == 0)
            throw new ArgumentException("At least one valid signal is required.", nameof(validSignals));

        this.centroids = centroids;
        this.bootstrapThreshold = bootstrapThreshold >= 0 ? bootstrapThreshold : throw new ArgumentOutOfRangeException(nameof(bootstrapThreshold));
        this.topK = topK > 0 ? topK : throw new ArgumentOutOfRangeException(nameof(topK));
        this.minNeighbors = minNeighbors > 0 ? minNeighbors : throw new ArgumentOutOfRangeException(nameof(minNeighbors));
        if (minNeighbors > topK) throw new ArgumentException("minNeighbors must be <= topK.", nameof(minNeighbors));
        this.minNeighborAgreement = minNeighborAgreement > 0 ? minNeighborAgreement : throw new ArgumentOutOfRangeException(nameof(minNeighborAgreement));
        if (minNeighborAgreement > minNeighbors) throw new ArgumentException("minNeighborAgreement must be <= minNeighbors.", nameof(minNeighborAgreement));
        this.minAvgSimilarity = minAvgSimilarity;
        this.minMargin = minMargin;
    }

    public static VectorClassifier Create(
        VectorStoreCollection<int, VectorIndexEntry> collection,
        IChatClient chatClient,
        string systemPrompt,
        IEnumerable<string> validSignals,
        CategoryCentroidTracker? centroids = null,
        int bootstrapThreshold = DefaultBootstrapThreshold)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentException.ThrowIfNullOrEmpty(systemPrompt);

        return new VectorClassifier(
            collection,
            (item, ct) => RssClassifier.ClassifyAsync(chatClient, item, systemPrompt, ct),
            validSignals,
            centroids,
            bootstrapThreshold);
    }

    public async Task<ClassificationDecision> ClassifyAsync(
        RssItem item,
        ReadOnlyMemory<float> embedding,
        int totalClassifiedCount,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (embedding.IsEmpty) throw new ArgumentException("Embedding must not be empty.", nameof(embedding));

        if (totalClassifiedCount < bootstrapThreshold)
        {
            var bootstrapResult = await llmFallback(item, ct).ConfigureAwait(false);
            return new ClassificationDecision(bootstrapResult, ClassificationSource.Bootstrap, NeighborStats.Empty);
        }

        var hits = new List<(string Signal, float Score)>(topK);
        await foreach (var hit in collection.SearchAsync(embedding, top: topK, cancellationToken: ct).ConfigureAwait(false))
        {
            hits.Add((hit.Record.Signal, (float)(hit.Score ?? 0.0)));
            if (hits.Count >= topK) break;
        }

        if (hits.Count < minNeighbors)
        {
            var sparse = await llmFallback(item, ct).ConfigureAwait(false);
            return new ClassificationDecision(sparse, ClassificationSource.LlmSparseNeighbors, NeighborStats.From(hits));
        }

        var top = hits.Take(minNeighbors).ToList();
        var stats = NeighborStats.From(top);

        var passesGates =
            validSignals.Contains(stats.TopSignal) &&
            stats.AverageSimilarity >= minAvgSimilarity &&
            stats.TopSignalAgreement >= minNeighborAgreement &&
            stats.Margin >= minMargin;

        if (!passesGates)
        {
            var lowConfidence = await llmFallback(item, ct).ConfigureAwait(false);
            return new ClassificationDecision(lowConfidence, ClassificationSource.LlmLowConfidence, stats);
        }

        var centroidAgreement = TryGetCentroidAgreement(stats.TopSignal, embedding);
        var reasoning =
            $"vector-auto: top={stats.TopSignal} avgSim={stats.AverageSimilarity:F3} " +
            $"agreement={stats.TopSignalAgreement}/{top.Count} margin={stats.Margin:F3}" +
            (centroidAgreement is null ? "" : $" centroid={centroidAgreement.Value.Signal}@{centroidAgreement.Value.Score:F3}");

        var auto = new ClassificationResult(stats.TopSignal, reasoning, IsNoise: false);
        return new ClassificationDecision(auto, ClassificationSource.VectorAuto, stats);
    }

    (string Signal, float Score)? TryGetCentroidAgreement(string topSignal, ReadOnlyMemory<float> embedding)
    {
        if (centroids is null) return null;
        var match = centroids.GetBestCentroidMatch(embedding, minSimilarity: 0.0f);
        if (match is null) return null;
        return string.Equals(match.Value.Signal, topSignal, StringComparison.OrdinalIgnoreCase)
            ? match
            : null;
    }
}

public enum ClassificationSource
{
    Bootstrap,
    VectorAuto,
    LlmSparseNeighbors,
    LlmLowConfidence,
    LlmEmbeddingFailed,
    Failed,
}

public sealed record ClassificationDecision(
    ClassificationResult Result,
    ClassificationSource Source,
    NeighborStats Stats)
{
    public bool WasAutoLabelled => Source == ClassificationSource.VectorAuto;
}

public sealed record NeighborStats(
    int NeighborCount,
    string TopSignal,
    int TopSignalAgreement,
    float AverageSimilarity,
    float Margin)
{
    public static readonly NeighborStats Empty = new(0, string.Empty, 0, 0f, 0f);

    public static NeighborStats From(IReadOnlyList<(string Signal, float Score)> hits)
    {
        if (hits.Count == 0) return Empty;

        var byCategory = hits
            .GroupBy(h => h.Signal, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Signal: g.Key, Avg: g.Average(h => h.Score), Count: g.Count()))
            .OrderByDescending(g => g.Avg)
            .ThenByDescending(g => g.Count)
            .ToList();

        var top = byCategory[0];
        var second = byCategory.Count > 1 ? byCategory[1] : (Signal: string.Empty, Avg: 0f, Count: 0);

        return new NeighborStats(
            NeighborCount: hits.Count,
            TopSignal: top.Signal,
            TopSignalAgreement: top.Count,
            AverageSimilarity: top.Avg,
            Margin: top.Avg - second.Avg);
    }
}
