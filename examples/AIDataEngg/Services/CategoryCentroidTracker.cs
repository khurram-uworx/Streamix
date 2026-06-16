using System.Collections.Concurrent;
using System.Numerics.Tensors;

namespace AIDataEngg.Services;

// Maintains a running-mean ("centroid") embedding per signal category.
// Used by VectorClassifier as a complementary confidence signal alongside
// nearest-neighbour vector search (Plan2 Task 4).
//
// Threading: ConcurrentDictionary owns entry lookup; per-entry lock owns
// the running-average mutation so count and mean stay consistent.
public sealed class CategoryCentroidTracker
{
    private sealed class Centroid
    {
        public readonly float[] Mean;
        public int Count;

        public Centroid(int dimensions)
        {
            Mean = new float[dimensions];
        }
    }

    private readonly ConcurrentDictionary<string, Centroid> centroids;
    private int expectedDimensions;

    public CategoryCentroidTracker()
    {
        centroids = new ConcurrentDictionary<string, Centroid>(StringComparer.OrdinalIgnoreCase);
        expectedDimensions = 0;
    }

    public int CategoryCount => centroids.Count;

    public int GetCount(string signal)
    {
        ArgumentException.ThrowIfNullOrEmpty(signal);
        return centroids.TryGetValue(signal, out var entry) ? Volatile.Read(ref entry.Count) : 0;
    }

    public void AddOrUpdate(string signal, ReadOnlyMemory<float> embedding)
    {
        ArgumentException.ThrowIfNullOrEmpty(signal);
        if (embedding.IsEmpty) throw new ArgumentException("Embedding must not be empty.", nameof(embedding));

        // First non-empty embedding pins the dimensionality; subsequent inserts must match.
        var dims = embedding.Length;
        var prior = Interlocked.CompareExchange(ref expectedDimensions, dims, 0);
        if (prior != 0 && prior != dims)
        {
            throw new ArgumentException(
                $"Embedding dimension {dims} does not match tracker dimension {prior}.",
                nameof(embedding));
        }

        var normalized = Normalize(embedding.Span);

        var entry = centroids.GetOrAdd(signal, _ => new Centroid(dims));
        lock (entry)
        {
            var n = entry.Count;
            var mean = entry.Mean;
            for (var i = 0; i < dims; i++)
            {
                mean[i] = (mean[i] * n + normalized[i]) / (n + 1);
            }
            entry.Count = n + 1;
        }
    }

    public float GetCentroidSimilarity(string signal, ReadOnlyMemory<float> embedding)
    {
        ArgumentException.ThrowIfNullOrEmpty(signal);
        if (!centroids.TryGetValue(signal, out var entry)) return 0f;

        float[] snapshot;
        lock (entry)
        {
            if (entry.Count == 0) return 0f;
            snapshot = (float[])entry.Mean.Clone();
        }
        return CosineSimilarity(snapshot, embedding.Span);
    }

    public (string Signal, float Score)? GetBestCentroidMatch(
        ReadOnlyMemory<float> embedding,
        float minSimilarity = 0.7f)
    {
        if (embedding.IsEmpty) return null;

        string? bestSignal = null;
        var bestScore = float.NegativeInfinity;

        foreach (var (signal, entry) in centroids)
        {
            float[] snapshot;
            lock (entry)
            {
                if (entry.Count == 0) continue;
                snapshot = (float[])entry.Mean.Clone();
            }
            var score = CosineSimilarity(snapshot, embedding.Span);
            if (score > bestScore)
            {
                bestScore = score;
                bestSignal = signal;
            }
        }

        if (bestSignal is null || bestScore < minSimilarity) return null;
        return (bestSignal, bestScore);
    }

    private static float[] Normalize(ReadOnlySpan<float> source)
    {
        var copy = new float[source.Length];
        var sumSq = 0.0;
        for (var i = 0; i < source.Length; i++) sumSq += source[i] * source[i];
        if (sumSq == 0.0)
        {
            // Zero vector stays zero; cosine against it is 0 by convention below.
            return copy;
        }
        var norm = (float)Math.Sqrt(sumSq);
        for (var i = 0; i < source.Length; i++) copy[i] = source[i] / norm;
        return copy;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return 0f;
        var similarity = TensorPrimitives.CosineSimilarity(a, b);
        return float.IsNaN(similarity) ? 0f : similarity;
    }
}
