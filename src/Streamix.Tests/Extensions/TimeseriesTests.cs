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
}
