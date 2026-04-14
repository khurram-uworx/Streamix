using NUnit.Framework;
using Streamix;

namespace Streamix.Tests.Extensions;

[TestFixture]
public class TimeseriesTests
{
    [Test]
    public void Timestamped_ShouldHoldValueAndTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = new Timestamped<int>(42, now);

        Assert.That(ts.Value, Is.EqualTo(42));
        Assert.That(ts.Timestamp, Is.EqualTo(now));
    }

    [Test]
    public void Timestamped_Create_ShouldCreateInstance()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = Timestamped.Create(42, now);

        Assert.That(ts.Value, Is.EqualTo(42));
        Assert.That(ts.Timestamp, Is.EqualTo(now));
    }

    [Test]
    public void Timestamped_Equality_ShouldWork()
    {
        var now = DateTimeOffset.UtcNow;
        var ts1 = new Timestamped<int>(42, now);
        var ts2 = new Timestamped<int>(42, now);
        var ts3 = new Timestamped<int>(43, now);

        Assert.That(ts1, Is.EqualTo(ts2));
        Assert.That(ts1, Is.Not.EqualTo(ts3));
    }

    [Test]
    public async Task WindowByTime_Tumbling_ShouldGroupItemsCorrectly()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromMinutes(10);

        var items = new[]
        {
            Timestamped.Create(1, start.AddMinutes(1)),
            Timestamped.Create(2, start.AddMinutes(5)),
            Timestamped.Create(3, start.AddMinutes(11)),
            Timestamped.Create(4, start.AddMinutes(15)),
            Timestamped.Create(5, start.AddMinutes(21)),
        };

        var source = Stream.From(items);
        var windows = source.WindowByTime(duration);

        var windowList = new List<List<Timestamped<int>>>();
        await foreach (var window in windows)
        {
            var list = new List<Timestamped<int>>();
            await foreach (var item in window)
            {
                list.Add(item);
            }
            windowList.Add(list);
        }

        Assert.That(windowList, Has.Count.EqualTo(3));
        Assert.That(windowList[0].Select(x => x.Value), Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(windowList[1].Select(x => x.Value), Is.EquivalentTo(new[] { 3, 4 }));
        Assert.That(windowList[2].Select(x => x.Value), Is.EquivalentTo(new[] { 5 }));
    }

    [Test]
    public async Task WindowByTime_Tumbling_ShouldHandleExactBoundaries()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromMinutes(10);

        var items = new[]
        {
            Timestamped.Create(1, start), // Inclusive
            Timestamped.Create(2, start.AddMinutes(10)), // Exclusive for first, inclusive for second
        };

        var source = Stream.From(items);
        var windows = source.WindowByTime(duration);

        var windowList = new List<List<Timestamped<int>>>();
        await foreach (var window in windows)
        {
            var list = new List<Timestamped<int>>();
            await foreach (var item in window)
            {
                list.Add(item);
            }
            windowList.Add(list);
        }

        Assert.That(windowList, Has.Count.EqualTo(2));
        Assert.That(windowList[0].Select(x => x.Value), Is.EquivalentTo(new[] { 1 }));
        Assert.That(windowList[1].Select(x => x.Value), Is.EquivalentTo(new[] { 2 }));
    }

    [Test]
    public async Task WindowByTime_Tumbling_ShouldHandleEmptyStream()
    {
        var source = Stream.Empty<Timestamped<int>>();
        var windows = source.WindowByTime(TimeSpan.FromMinutes(10));

        var count = 0;
        await foreach (var window in windows)
        {
            count++;
        }

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task WindowByTime_Tumbling_ShouldHandleSingleItem()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var items = new[] { Timestamped.Create(1, start) };

        var source = Stream.From(items);
        var windows = source.WindowByTime(TimeSpan.FromMinutes(10));

        var windowList = new List<List<Timestamped<int>>>();
        await foreach (var window in windows)
        {
            var list = new List<Timestamped<int>>();
            await foreach (var item in window)
            {
                list.Add(item);
            }
            windowList.Add(list);
        }

        Assert.That(windowList, Has.Count.EqualTo(1));
        Assert.That(windowList[0].Select(x => x.Value), Is.EquivalentTo(new[] { 1 }));
    }

    [Test]
    public async Task WindowByTime_Sliding_ShouldGroupItemsCorrectly()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromMinutes(10);
        var slide = TimeSpan.FromMinutes(5);

        var items = new[]
        {
            Timestamped.Create(1, start.AddMinutes(1)),
            Timestamped.Create(2, start.AddMinutes(6)),
            Timestamped.Create(3, start.AddMinutes(11)),
        };

        // Window 1: [0, 10) -> items 1, 2
        // Window 2: [5, 15) -> items 2, 3
        // Window 3: [10, 20) -> item 3

        var source = Stream.From(items);
        var windows = source.WindowByTime(duration, slide);

        var windowList = new List<List<Timestamped<int>>>();
        var tasks = new List<Task>();
        await foreach (var window in windows)
        {
            var w = window;
            tasks.Add(Task.Run(async () =>
            {
                var list = new List<Timestamped<int>>();
                await foreach (var item in w)
                {
                    list.Add(item);
                }
                lock (windowList) windowList.Add(list);
            }));
        }
        await Task.WhenAll(tasks);

        windowList = windowList.Where(w => w.Count > 0).OrderBy(w => w[0].Timestamp.UtcTicks).ThenByDescending(w => w.Count).ToList();

        Assert.That(windowList, Has.Count.EqualTo(3));
        Assert.That(windowList[0].Select(x => x.Value), Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(windowList[1].Select(x => x.Value), Is.EquivalentTo(new[] { 2, 3 }));
        Assert.That(windowList[2].Select(x => x.Value), Is.EquivalentTo(new[] { 3 }));
    }

    [Test]
    public async Task WindowByTime_Sliding_SparseSliding_ShouldHaveGaps()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromMinutes(5);
        var slide = TimeSpan.FromMinutes(10);

        var items = new[]
        {
            Timestamped.Create(1, start.AddMinutes(1)),
            Timestamped.Create(2, start.AddMinutes(6)), // In gap [5, 10)
            Timestamped.Create(3, start.AddMinutes(11)),
        };

        // Window 1: [0, 5) -> item 1
        // Window 2: [10, 15) -> item 3

        var source = Stream.From(items);
        var windows = source.WindowByTime(duration, slide);

        var windowList = new List<List<Timestamped<int>>>();
        var tasks = new List<Task>();
        await foreach (var window in windows)
        {
            var w = window;
            tasks.Add(Task.Run(async () =>
            {
                var list = new List<Timestamped<int>>();
                await foreach (var item in w)
                {
                    list.Add(item);
                }
                lock (windowList) windowList.Add(list);
            }));
        }
        await Task.WhenAll(tasks);

        windowList = windowList.Where(w => w.Count > 0).OrderBy(w => w[0].Timestamp.UtcTicks).ToList();

        Assert.That(windowList, Has.Count.EqualTo(2));
        Assert.That(windowList[0].Select(x => x.Value), Is.EquivalentTo(new[] { 1 }));
        Assert.That(windowList[1].Select(x => x.Value), Is.EquivalentTo(new[] { 3 }));
    }

    [Test]
    public async Task WindowByTime_Sliding_ExactBoundaries_ShouldHandleInclusionCorrectly()
    {
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromMinutes(10);
        var slide = TimeSpan.FromMinutes(5);

        var items = new[]
        {
            Timestamped.Create(1, start), // Start of Window 1
            Timestamped.Create(2, start.AddMinutes(5)), // Start of Window 2, middle of Window 1
            Timestamped.Create(3, start.AddMinutes(10)), // Start of Window 3, end of Window 1 (exclusive)
        };

        // Window 1: [0, 10) -> items 1, 2
        // Window 2: [5, 15) -> items 2, 3
        // Window 3: [10, 20) -> item 3

        var source = Stream.From(items);
        var windows = source.WindowByTime(duration, slide);

        var windowList = new List<List<Timestamped<int>>>();
        var tasks = new List<Task>();
        await foreach (var window in windows)
        {
            var w = window;
            tasks.Add(Task.Run(async () =>
            {
                var list = new List<Timestamped<int>>();
                await foreach (var item in w)
                {
                    list.Add(item);
                }
                lock (windowList) windowList.Add(list);
            }));
        }
        await Task.WhenAll(tasks);

        windowList = windowList.Where(w => w.Count > 0).OrderBy(w => w[0].Timestamp.UtcTicks).ThenByDescending(w => w.Count).ToList();

        Assert.That(windowList, Has.Count.EqualTo(3));
        Assert.That(windowList[0].Select(x => x.Value), Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(windowList[1].Select(x => x.Value), Is.EquivalentTo(new[] { 2, 3 }));
        Assert.That(windowList[2].Select(x => x.Value), Is.EquivalentTo(new[] { 3 }));
    }
}
