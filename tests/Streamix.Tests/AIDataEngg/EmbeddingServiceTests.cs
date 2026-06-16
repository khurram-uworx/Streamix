using AIDataEngg.Models;
using AIDataEngg.Services;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Streamix.Tests.AIDataEngg;

[TestFixture]
public class EmbeddingServiceTests
{
    [Test]
    public async Task GenerateAsync_RssItem_ConcatenatesTitleAndSummary()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake);

        var vector = await sut.GenerateAsync(new RssItem { Title = "Title", Summary = "Body" });

        Assert.That(fake.CapturedInputs, Has.Count.EqualTo(1));
        Assert.That(fake.CapturedInputs[0], Is.EqualTo("Title\n\nBody"));
        Assert.That(vector.Length, Is.EqualTo(3));
    }

    [Test]
    public async Task GenerateAsync_RssItem_NoSummary_SendsTitleOnly()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake);

        await sut.GenerateAsync(new RssItem { Title = "Only title", Summary = null });

        Assert.That(fake.CapturedInputs[0], Is.EqualTo("Only title"));
    }

    [Test]
    public async Task GenerateAsync_RssItem_WhitespaceSummary_TreatedAsMissing()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake);

        await sut.GenerateAsync(new RssItem { Title = "Title", Summary = "   " });

        Assert.That(fake.CapturedInputs[0], Is.EqualTo("Title"));
    }

    [Test]
    public async Task GenerateAsync_TruncatesLongInput()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake, maxInputChars: 10);

        await sut.GenerateAsync(new string('x', 100));

        Assert.That(fake.CapturedInputs[0], Has.Length.EqualTo(10));
    }

    [Test]
    public async Task GenerateAsync_ShortInput_NotTruncated()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake, maxInputChars: 100);

        await sut.GenerateAsync("short");

        Assert.That(fake.CapturedInputs[0], Is.EqualTo("short"));
    }

    [Test]
    public void GenerateAsync_AlreadyCancelled_Throws()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sut.GenerateAsync("anything", cts.Token));
    }

    [Test]
    public async Task GenerateAsync_PropagatesCancellationTokenToGenerator()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake);
        using var cts = new CancellationTokenSource();

        await sut.GenerateAsync("anything", cts.Token);

        Assert.That(fake.LastSeenCancellationToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public void Constructor_NullGenerator_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EmbeddingService(null!));
    }

    [Test]
    public void Constructor_NonPositiveMaxChars_Throws()
    {
        var fake = new CapturingEmbeddingGenerator();
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbeddingService(fake, maxInputChars: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbeddingService(fake, maxInputChars: -1));
    }

    [Test]
    public void GenerateAsync_NullItem_Throws()
    {
        var fake = new CapturingEmbeddingGenerator();
        var sut = new EmbeddingService(fake);

        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.GenerateAsync((RssItem)null!));
    }

    private sealed class CapturingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string> CapturedInputs { get; } = new();
        public CancellationToken LastSeenCancellationToken { get; private set; }

        private static readonly float[] FixedVector = [1f, 2f, 3f];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastSeenCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var v in values)
            {
                CapturedInputs.Add(v);
                result.Add(new Embedding<float>(FixedVector));
            }
            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
