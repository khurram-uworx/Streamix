using NUnit.Framework;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace Streamix.Tests.AIDataEngg;

[TestFixture]
public class VectorClassifierTests
{
    private static readonly string[] DefaultValidSignals =
    {
        "AI/ML", "Security", "Cloud Infrastructure", "Developer Tools", "Open Source", "General"
    };

    [Test]
    public void Defaults_AreExposedAsPublicConstants()
    {
        Assert.That(VectorClassifier.DefaultBootstrapThreshold, Is.EqualTo(20));
        Assert.That(VectorClassifier.DefaultTopK, Is.EqualTo(10));
        Assert.That(VectorClassifier.DefaultMinNeighbors, Is.EqualTo(5));
        Assert.That(VectorClassifier.DefaultMinNeighborAgreement, Is.EqualTo(5));
        Assert.That(VectorClassifier.DefaultMinAvgSimilarity, Is.EqualTo(0.86f));
        Assert.That(VectorClassifier.DefaultMinMargin, Is.EqualTo(0.10f));
    }

    [Test]
    public async Task Bootstrap_WhenUnderThreshold_UsesLlmFallback()
    {
        var col = await CreateCollectionAsync();
        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals);

        var decision = await sut.ClassifyAsync(
            new RssItem { Title = "anything" },
            MakeUnitVector(seed: 1),
            totalClassifiedCount: 5);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.Bootstrap));
        Assert.That(decision.Result.Signal, Is.EqualTo(StubLlm.DefaultSignal));
        Assert.That(decision.WasAutoLabelled, Is.False);
        Assert.That(llm.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EmptyStore_PostBootstrap_FallsBackToLlmAsSparseNeighbors()
    {
        var col = await CreateCollectionAsync();
        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(
            new RssItem { Title = "x" },
            MakeUnitVector(seed: 1),
            totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.LlmSparseNeighbors));
        Assert.That(decision.Stats.NeighborCount, Is.EqualTo(0));
        Assert.That(llm.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task FewerThanMinNeighbors_FallsBackToLlmAsSparse()
    {
        var col = await CreateCollectionAsync();
        var anchor = MakeUnitVector(seed: 1);
        await SeedAsync(col,
            (1, "AI/ML", Perturb(anchor, 0.01f, 10)),
            (2, "AI/ML", Perturb(anchor, 0.01f, 11)),
            (3, "AI/ML", Perturb(anchor, 0.01f, 12)));

        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(new RssItem { Title = "x" }, anchor, totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.LlmSparseNeighbors));
        Assert.That(decision.Stats.NeighborCount, Is.EqualTo(3));
        Assert.That(llm.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task HighConfidence_AutoLabelsWithoutLlm()
    {
        var col = await CreateCollectionAsync();
        var anchor = MakeUnitVector(seed: 1);

        var entries = Enumerable.Range(1, 10)
            .Select(i => (i, "AI/ML", Perturb(anchor, 0.02f, seed: i + 100)))
            .ToArray();
        await SeedAsync(col, entries);

        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(new RssItem { Title = "x" }, anchor, totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.VectorAuto));
        Assert.That(decision.Result.Signal, Is.EqualTo("AI/ML"));
        Assert.That(decision.WasAutoLabelled, Is.True);
        Assert.That(decision.Result.IsNoise, Is.False);
        Assert.That(decision.Stats.AverageSimilarity, Is.GreaterThanOrEqualTo(0.84f));
        Assert.That(decision.Stats.TopSignalAgreement, Is.EqualTo(5));
        Assert.That(llm.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task LowAverageSimilarity_FallsBackToLlm()
    {
        var col = await CreateCollectionAsync();
        var anchor = MakeUnitVector(seed: 1);
        var queryUnrelated = MakeUnitVector(seed: 9999);

        var entries = Enumerable.Range(1, 10)
            .Select(i => (i, "AI/ML", Perturb(anchor, 0.02f, seed: i + 100)))
            .ToArray();
        await SeedAsync(col, entries);

        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(new RssItem { Title = "x" }, queryUnrelated, totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.LlmLowConfidence));
        Assert.That(decision.Stats.AverageSimilarity, Is.LessThan(0.84f));
        Assert.That(llm.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task LowNeighborAgreement_FallsBackToLlm()
    {
        var col = await CreateCollectionAsync();
        var anchor = MakeUnitVector(seed: 1);

        // Mixed signals near anchor: AI/ML wins by count but agreement < minNeighborAgreement(5).
        await SeedAsync(col,
            (1, "AI/ML", Perturb(anchor, 0.01f, 100)),
            (2, "Security", Perturb(anchor, 0.01f, 101)),
            (3, "AI/ML", Perturb(anchor, 0.01f, 102)),
            (4, "Security", Perturb(anchor, 0.01f, 103)),
            (5, "AI/ML", Perturb(anchor, 0.01f, 104)),
            (6, "Security", Perturb(anchor, 0.01f, 105)),
            (7, "AI/ML", Perturb(anchor, 0.01f, 106)));

        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(new RssItem { Title = "x" }, anchor, totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.LlmLowConfidence));
        Assert.That(decision.Stats.TopSignalAgreement, Is.LessThan(5));
        Assert.That(llm.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TopSignalNotInValidSignals_FallsBackToLlm()
    {
        var col = await CreateCollectionAsync();
        var anchor = MakeUnitVector(seed: 1);

        var entries = Enumerable.Range(1, 10)
            .Select(i => (i, "RetiredSignal", Perturb(anchor, 0.02f, seed: i + 100)))
            .ToArray();
        await SeedAsync(col, entries);

        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(new RssItem { Title = "x" }, anchor, totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.LlmLowConfidence));
        Assert.That(llm.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task WhenCentroidsAgree_ReasoningIncludesCentroidScore()
    {
        var col = await CreateCollectionAsync();
        var anchor = MakeUnitVector(seed: 1);

        var entries = Enumerable.Range(1, 10)
            .Select(i => (i, "AI/ML", Perturb(anchor, 0.02f, seed: i + 100)))
            .ToArray();
        await SeedAsync(col, entries);

        var centroids = new CategoryCentroidTracker();
        foreach (var (_, sig, vec) in entries) centroids.AddOrUpdate(sig, vec);

        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, centroids: centroids, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(new RssItem { Title = "x" }, anchor, totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.VectorAuto));
        Assert.That(decision.Result.Reasoning, Does.Contain("centroid="));
    }

    [Test]
    public async Task WhenCentroidsAreEmpty_ReasoningOmitsCentroid()
    {
        var col = await CreateCollectionAsync();
        var anchor = MakeUnitVector(seed: 1);

        var entries = Enumerable.Range(1, 10)
            .Select(i => (i, "AI/ML", Perturb(anchor, 0.02f, seed: i + 100)))
            .ToArray();
        await SeedAsync(col, entries);

        var centroids = new CategoryCentroidTracker(); // empty
        var llm = new StubLlm();
        var sut = new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, centroids: centroids, bootstrapThreshold: 0);

        var decision = await sut.ClassifyAsync(new RssItem { Title = "x" }, anchor, totalClassifiedCount: 100);

        Assert.That(decision.Source, Is.EqualTo(ClassificationSource.VectorAuto));
        Assert.That(decision.Result.Reasoning, Does.Not.Contain("centroid="));
    }

    [Test]
    public async Task Constructor_NullArgs_Throw()
    {
        var col = await CreateCollectionAsync();
        StubLlm llm = new();
        Assert.Throws<ArgumentNullException>(() => new VectorClassifier(null!, llm.ClassifyAsync, DefaultValidSignals));
        Assert.Throws<ArgumentNullException>(() => new VectorClassifier(col, null!, DefaultValidSignals));
        Assert.Throws<ArgumentNullException>(() => new VectorClassifier(col, llm.ClassifyAsync, null!));
    }

    [Test]
    public async Task Constructor_InvalidNumericArgs_Throw()
    {
        var col = await CreateCollectionAsync();
        StubLlm llm = new();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, bootstrapThreshold: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, topK: 0));
        Assert.Throws<ArgumentException>(() =>
            new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, topK: 5, minNeighbors: 10));
        Assert.Throws<ArgumentException>(() =>
            new VectorClassifier(col, llm.ClassifyAsync, DefaultValidSignals, minNeighbors: 5, minNeighborAgreement: 10));
        Assert.Throws<ArgumentException>(() =>
            new VectorClassifier(col, llm.ClassifyAsync, Array.Empty<string>()));
    }

    [Test]
    public async Task ClassifyAsync_NullItem_Throws()
    {
        var col = await CreateCollectionAsync();
        var sut = new VectorClassifier(col, new StubLlm().ClassifyAsync, DefaultValidSignals);

        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.ClassifyAsync(null!, MakeUnitVector(1), totalClassifiedCount: 0));
    }

    [Test]
    public async Task ClassifyAsync_EmptyEmbedding_Throws()
    {
        var col = await CreateCollectionAsync();
        var sut = new VectorClassifier(col, new StubLlm().ClassifyAsync, DefaultValidSignals);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.ClassifyAsync(new RssItem { Title = "x" }, ReadOnlyMemory<float>.Empty, totalClassifiedCount: 0));
    }

    private static Task<Microsoft.Extensions.VectorData.VectorStoreCollection<int, VectorIndexEntry>> CreateCollectionAsync()
        => VectorStoreProvider.GetOrCreateCollectionAsync($"test-{Guid.NewGuid():N}");

    private static async Task SeedAsync(
        Microsoft.Extensions.VectorData.VectorStoreCollection<int, VectorIndexEntry> col,
        params (int Id, string Signal, ReadOnlyMemory<float> Embedding)[] entries)
    {
        var records = entries.Select(e => new VectorIndexEntry
        {
            Id = e.Id,
            RssItemId = e.Id,
            Signal = e.Signal,
            Title = $"Item {e.Id}",
            Summary = null,
            Embedding = e.Embedding,
        });
        await col.UpsertAsync(records);
    }

    private static ReadOnlyMemory<float> MakeUnitVector(int seed, int dimensions = 768)
    {
        var rng = new Random(seed);
        var v = new float[dimensions];
        var sumSq = 0.0;
        for (var i = 0; i < dimensions; i++)
        {
            v[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            sumSq += v[i] * v[i];
        }
        var norm = (float)Math.Sqrt(sumSq);
        for (var i = 0; i < dimensions; i++) v[i] /= norm;
        return v;
    }

    private static ReadOnlyMemory<float> Perturb(ReadOnlyMemory<float> source, float magnitude, int seed)
    {
        var rng = new Random(seed);
        var src = source.Span;
        var v = new float[src.Length];
        var sumSq = 0.0;
        for (var i = 0; i < src.Length; i++)
        {
            v[i] = src[i] + (float)((rng.NextDouble() * 2.0 - 1.0) * magnitude);
            sumSq += v[i] * v[i];
        }
        var norm = (float)Math.Sqrt(sumSq);
        for (var i = 0; i < src.Length; i++) v[i] /= norm;
        return v;
    }

    private sealed class StubLlm
    {
        public const string DefaultSignal = "Fallback";

        public int CallCount;
        public ClassificationResult Result { get; init; } =
            new ClassificationResult(DefaultSignal, "stub", IsNoise: false);

        public Task<ClassificationResult> ClassifyAsync(RssItem item, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult(Result);
        }
    }
}
