using AIDataEngg.Data;
using AIDataEngg.Models;
using AIDataEngg.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.VectorData;
using NUnit.Framework;

namespace Streamix.Tests.AIDataEngg;

[TestFixture]
public class UserFeedbackServiceTests
{
    private string dbPath = null!;
    private VectorStoreCollection<int, VectorIndexEntry> collection = null!;

    [SetUp]
    public async Task SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"feedback-{Guid.NewGuid():N}.db");
        await using var seed = CreateContext();
        await seed.Database.EnsureCreatedAsync();
        collection = await VectorStoreProvider.GetOrCreateCollectionAsync($"feedback-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* best-effort */ }
    }

    [Test]
    public async Task EmptyInput_PrintsRecentAndHelp_ThenExits()
    {
        await SeedClassificationAsync(id: 1, signal: "AI/ML", title: "Hello", embedding: null);

        var input = new StringReader("");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        var rendered = output.ToString();
        Assert.That(rendered, Does.Contain("Recent 1 classifications"));
        Assert.That(rendered, Does.Contain("[1] AI/ML"));
        Assert.That(rendered, Does.Contain("Commands:"));
    }

    [Test]
    public async Task QuitCommand_StopsLoop()
    {
        var input = new StringReader("quit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        Assert.That(sut.LikedIds, Is.Empty);
        Assert.That(sut.HiddenIds, Is.Empty);
    }

    [Test]
    public async Task LikeCommand_AddsToLikedIds()
    {
        var input = new StringReader("like 5\nquit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        Assert.That(sut.LikedIds, Is.EquivalentTo(new[] { 5 }));
        Assert.That(output.ToString(), Does.Contain("liked id=5"));
    }

    [Test]
    public async Task HideCommand_SuppressesItemFromRecentListing()
    {
        await SeedClassificationAsync(1, "AI/ML", "Item-A", null);
        await SeedClassificationAsync(2, "Security", "Item-B", null);
        await SeedClassificationAsync(3, "Open Source", "Item-C", null);

        var input = new StringReader("hide 2\nrecent\nquit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        Assert.That(sut.HiddenIds, Does.Contain(2));
        var rendered = output.ToString();
        // After hide+recent, the second printout should not include id 2.
        var afterRecent = rendered.LastIndexOf("Recent ", StringComparison.Ordinal);
        Assert.That(afterRecent, Is.GreaterThan(0));
        var tail = rendered[afterRecent..];
        Assert.That(tail, Does.Not.Contain("[2]"));
        Assert.That(tail, Does.Contain("[1]"));
        Assert.That(tail, Does.Contain("[3]"));
    }

    [Test]
    public async Task MoreLike_NoSuchId_PrintsMessage()
    {
        var input = new StringReader("morelike 999\nquit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        Assert.That(output.ToString(), Does.Contain("No classification with id=999"));
    }

    [Test]
    public async Task MoreLike_NoEmbeddingStored_PrintsMessage()
    {
        await SeedClassificationAsync(1, "AI/ML", "Anchor", embedding: null);

        var input = new StringReader("morelike 1\nquit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        Assert.That(output.ToString(), Does.Contain("No embedding stored for id=1"));
    }

    [Test]
    public async Task MoreLike_WithSeededVectors_FindsSimilarItems()
    {
        var anchor = MakeUnitVector(seed: 1);

        // Seed three items in DB with the same embedding column.
        await SeedClassificationAsync(1, "AI/ML", "Anchor",     anchor);
        await SeedClassificationAsync(2, "AI/ML", "Near anchor", Perturb(anchor, 0.02f, 10));
        await SeedClassificationAsync(3, "Security", "Far",      MakeUnitVector(seed: 99));

        // Mirror those records into the in-memory vector store using the same ids.
        await collection.UpsertAsync(new[]
        {
            new VectorIndexEntry { Id = 1, RssItemId = 1, Signal = "AI/ML",   Title = "Anchor",     Embedding = anchor },
            new VectorIndexEntry { Id = 2, RssItemId = 2, Signal = "AI/ML",   Title = "Near anchor", Embedding = Perturb(anchor, 0.02f, 10) },
            new VectorIndexEntry { Id = 3, RssItemId = 3, Signal = "Security", Title = "Far",        Embedding = MakeUnitVector(seed: 99) },
        });

        var input = new StringReader("morelike 1\nquit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        var rendered = output.ToString();
        Assert.That(rendered, Does.Contain("Items similar to [1] Anchor"));
        Assert.That(rendered, Does.Contain("[2]")); // near-anchor included
        Assert.That(rendered, Does.Not.Contain("Items similar to [1] Anchor\n  (no similar items found)"));
    }

    [Test]
    public async Task UnknownCommand_PrintsHelp()
    {
        var input = new StringReader("frobnicate\nquit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        var rendered = output.ToString();
        Assert.That(rendered, Does.Contain("Unknown command: frobnicate"));
        Assert.That(rendered, Does.Contain("Commands:"));
    }

    [Test]
    public async Task HelpCommand_PrintsHelp()
    {
        var input = new StringReader("help\nquit\n");
        var output = new StringWriter();
        var sut = new UserFeedbackService(CreateContext, collection, input, output);

        await sut.RunInteractiveAsync();

        var rendered = output.ToString();
        // Initial print + help command print = at least two occurrences.
        var occurrences = CountOccurrences(rendered, "Commands:");
        Assert.That(occurrences, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void Constructor_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new UserFeedbackService(null!, collection));
        Assert.Throws<ArgumentNullException>(() => new UserFeedbackService(CreateContext, null!));
    }

    private RssDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RssDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new RssDbContext(options);
    }

    private async Task SeedClassificationAsync(int id, string signal, string title, ReadOnlyMemory<float>? embedding)
    {
        await using var db = CreateContext();
        var item = new RssItem
        {
            Title = title,
            Summary = null,
            ContentHash = $"hash-{id}",
            FeedName = "test",
            FeedUrl = "test",
        };
        db.RssItems.Add(item);
        await db.SaveChangesAsync();

        db.Classifications.Add(new ClassifiedRssItem
        {
            RssItemId = item.Id,
            Signal = signal,
            Reasoning = "seed",
            IsNoise = false,
            AttemptCount = 1,
            Embedding = embedding is null ? null : EmbeddingSerializer.ToBytes(embedding.Value),
        });
        await db.SaveChangesAsync();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
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
}
