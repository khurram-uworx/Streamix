using NUnit.Framework;

namespace Streamix.Tests;

[TestFixture]
public class StressTests
{
    [Test]
    public async Task FlatMap_HighConcurrency_LargeLoad()
    {
        const int itemCount = 10000;
        const int maxConcurrency = 100;
        var source = Flux.Range(1, itemCount);

        var result = await source
            .FlatMap(async x =>
            {
                await Task.Yield();
                return x;
            }, maxConcurrency: maxConcurrency)
            .ToListAsync();

        Assert.That(result.Count, Is.EqualTo(itemCount));
        Assert.That(result, Is.EquivalentTo(Enumerable.Range(1, itemCount)));
    }

    [Test]
    public async Task Merge_HighLoad()
    {
        const int streamCount = 50;
        const int itemsPerStream = 500;
        var streams = Enumerable.Range(0, streamCount)
            .Select(i => Flux.Range(i * itemsPerStream, itemsPerStream))
            .ToArray();

        var result = await Flux.Merge(streams).ToListAsync();

        Assert.That(result.Count, Is.EqualTo(streamCount * itemsPerStream));
        Assert.That(result, Is.EquivalentTo(Enumerable.Range(0, streamCount * itemsPerStream)));
    }

    [Test]
    public async Task Buffer_HighLoad()
    {
        const int itemCount = 100000;
        const int bufferSize = 100;
        var result = await Flux.Range(1, itemCount)
            .Buffer(bufferSize)
            .ToListAsync();

        Assert.That(result.Count, Is.EqualTo(itemCount / bufferSize));
        Assert.That(result.SelectMany(x => x).Count(), Is.EqualTo(itemCount));
    }

    [Test]
    public async Task HotStream_RefCount_HighChurn()
    {
        const int itemCount = 1000;
        var source = Flux.Range(1, itemCount).Delay(TimeSpan.FromMilliseconds(1));
        var shared = source.Publish().RefCount();

        async Task SubscribeAndConsume()
        {
            var list = await shared.Take(10).ToListAsync();
            Assert.That(list.Count, Is.GreaterThan(0));
        }

        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(SubscribeAndConsume());
            if (i % 10 == 0) await Task.Delay(5);
        }

        await Task.WhenAll(tasks);
    }
}
