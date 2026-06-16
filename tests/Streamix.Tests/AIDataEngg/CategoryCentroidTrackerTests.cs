using AIDataEngg.Services;
using NUnit.Framework;

namespace Streamix.Tests.AIDataEngg;

[TestFixture]
public class CategoryCentroidTrackerTests
{
    [Test]
    public void EmptyTracker_GetBestCentroidMatch_ReturnsNull()
    {
        var tracker = new CategoryCentroidTracker();
        var query = MakeUnitVector(seed: 1);

        Assert.That(tracker.GetBestCentroidMatch(query), Is.Null);
        Assert.That(tracker.CategoryCount, Is.EqualTo(0));
        Assert.That(tracker.GetCount("AI/ML"), Is.EqualTo(0));
        Assert.That(tracker.GetCentroidSimilarity("AI/ML", query), Is.EqualTo(0f));
    }

    [Test]
    public void SingleEmbedding_CentroidSimilarity_ReturnsOneForSameDirection()
    {
        var tracker = new CategoryCentroidTracker();
        var v = MakeUnitVector(seed: 42);

        tracker.AddOrUpdate("AI/ML", v);

        Assert.That(tracker.CategoryCount, Is.EqualTo(1));
        Assert.That(tracker.GetCount("AI/ML"), Is.EqualTo(1));
        Assert.That(tracker.GetCentroidSimilarity("AI/ML", v), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void RunningAverage_ConvergesToMean()
    {
        var tracker = new CategoryCentroidTracker();
        var v1 = MakeUnitVector(seed: 1);
        var v2 = Perturb(v1, magnitude: 0.05f, seed: 2);
        var v3 = Perturb(v1, magnitude: 0.05f, seed: 3);

        tracker.AddOrUpdate("AI/ML", v1);
        tracker.AddOrUpdate("AI/ML", v2);
        tracker.AddOrUpdate("AI/ML", v3);

        Assert.That(tracker.GetCount("AI/ML"), Is.EqualTo(3));
        // The centroid should be very close to v1 since v2 and v3 are perturbations of v1.
        Assert.That(tracker.GetCentroidSimilarity("AI/ML", v1), Is.GreaterThan(0.99f));
    }

    [Test]
    public void MultipleSignals_GetBestCentroidMatch_ReturnsClosest()
    {
        var tracker = new CategoryCentroidTracker();
        var aiAnchor = MakeUnitVector(seed: 100);
        var secAnchor = MakeUnitVector(seed: 999);

        tracker.AddOrUpdate("AI/ML", aiAnchor);
        tracker.AddOrUpdate("AI/ML", Perturb(aiAnchor, 0.05f, seed: 101));
        tracker.AddOrUpdate("Security", secAnchor);
        tracker.AddOrUpdate("Security", Perturb(secAnchor, 0.05f, seed: 1000));

        var probe = Perturb(aiAnchor, 0.05f, seed: 200);

        var match = tracker.GetBestCentroidMatch(probe, minSimilarity: 0.7f);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Value.Signal, Is.EqualTo("AI/ML"));
        Assert.That(match.Value.Score, Is.GreaterThan(0.95f));
    }

    [Test]
    public void GetBestCentroidMatch_BelowThreshold_ReturnsNull()
    {
        var tracker = new CategoryCentroidTracker();
        var anchor = MakeUnitVector(seed: 1);
        tracker.AddOrUpdate("AI/ML", anchor);

        // A vector orthogonal/random to the anchor will have similarity near 0.
        var unrelated = MakeUnitVector(seed: 9999);

        Assert.That(tracker.GetBestCentroidMatch(unrelated, minSimilarity: 0.7f), Is.Null);
    }

    [Test]
    public void DimensionMismatch_Throws()
    {
        var tracker = new CategoryCentroidTracker();
        tracker.AddOrUpdate("AI/ML", new float[] { 1f, 0f, 0f });

        Assert.Throws<ArgumentException>(() =>
            tracker.AddOrUpdate("AI/ML", new float[] { 1f, 0f }));
    }

    [Test]
    public void EmptyEmbedding_Throws()
    {
        var tracker = new CategoryCentroidTracker();
        Assert.Throws<ArgumentException>(() =>
            tracker.AddOrUpdate("AI/ML", ReadOnlyMemory<float>.Empty));
    }

    [Test]
    public void NullOrEmptySignal_Throws()
    {
        var tracker = new CategoryCentroidTracker();
        Assert.Throws<ArgumentException>(() => tracker.AddOrUpdate("", new float[] { 1f }));
        Assert.Throws<ArgumentNullException>(() => tracker.AddOrUpdate(null!, new float[] { 1f }));
    }

    [Test]
    public void SignalLookup_IsCaseInsensitive()
    {
        var tracker = new CategoryCentroidTracker();
        var v = MakeUnitVector(seed: 7);
        tracker.AddOrUpdate("AI/ML", v);

        Assert.That(tracker.GetCount("ai/ml"), Is.EqualTo(1));
        Assert.That(tracker.GetCentroidSimilarity("ai/ml", v), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public async Task ConcurrentUpdates_AreThreadSafe()
    {
        var tracker = new CategoryCentroidTracker();
        const int threadsPerSignal = 8;
        const int updatesPerThread = 50;

        var anchor = MakeUnitVector(seed: 1);

        var tasks = new List<Task>();
        for (var t = 0; t < threadsPerSignal; t++)
        {
            var seed = t;
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < updatesPerThread; i++)
                {
                    tracker.AddOrUpdate("AI/ML", Perturb(anchor, 0.02f, seed: seed * 1000 + i));
                }
            }));
        }
        await Task.WhenAll(tasks);

        Assert.That(tracker.GetCount("AI/ML"), Is.EqualTo(threadsPerSignal * updatesPerThread));
        // Centroid should still be close to anchor direction.
        Assert.That(tracker.GetCentroidSimilarity("AI/ML", anchor), Is.GreaterThan(0.98f));
    }

    private static ReadOnlyMemory<float> MakeUnitVector(int seed, int dimensions = 64)
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
}
