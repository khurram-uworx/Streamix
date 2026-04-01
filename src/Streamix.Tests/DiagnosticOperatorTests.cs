using NUnit.Framework;

namespace Streamix.Tests;

[TestFixture]
public class DiagnosticOperatorTests
{
    [Test]
    public async Task Stream_DoOnNext_ExecutesForEveryItem()
    {
        var items = new List<int>();
        var result = await Stream.Range(1, 5)
            .DoOnNext(x => items.Add(x))
            .Select(x => x)
            .ToListAsync();

        Assert.That(items, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void Stream_DoOnError_ExecutesUponStreamFailure()
    {
        var exception = new Exception("Test error");
        Exception? caught = null;

        var stream = Stream.Error<int>(exception)
            .DoOnError(ex => caught = ex);

        Assert.ThrowsAsync<Exception>(async () => await stream.ToListAsync());
        Assert.That(caught, Is.SameAs(exception));
    }

    [Test]
    public async Task Stream_DoOnTerminate_ExecutesUponSuccessfulCompletion()
    {
        bool terminated = false;
        await Stream.Range(1, 3)
            .DoOnTerminate(() => terminated = true)
            .ToListAsync();

        Assert.That(terminated, Is.True);
    }

    [Test]
    public void Stream_DoOnTerminate_ExecutesUponError()
    {
        bool terminated = false;
        var stream = Stream.Error<int>(new Exception())
            .DoOnTerminate(() => terminated = true);

        Assert.ThrowsAsync<Exception>(async () => await stream.ToListAsync());
        Assert.That(terminated, Is.True);
    }

    [Test]
    public async Task Single_DoOnNext_ExecutesForItem()
    {
        int value = 0;
        var result = await Single.From(42)
            .DoOnNext(x => value = x)
            .ToTask();

        Assert.That(value, Is.EqualTo(42));
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void Single_DoOnError_ExecutesUponFailure()
    {
        var exception = new Exception("Test error");
        Exception? caught = null;

        var single = Single.Error<int>(exception)
            .DoOnError(ex => caught = ex);

        Assert.ThrowsAsync<Exception>(async () => await single.ToTask());
        Assert.That(caught, Is.SameAs(exception));
    }

    [Test]
    public async Task Single_DoOnTerminate_ExecutesUponSuccessfulCompletion()
    {
        bool terminated = false;
        await Single.From(42)
            .DoOnTerminate(() => terminated = true)
            .ToTask();

        Assert.That(terminated, Is.True);
    }

    [Test]
    public void Single_DoOnTerminate_ExecutesUponError()
    {
        bool terminated = false;
        var single = Single.Error<int>(new Exception())
            .DoOnTerminate(() => terminated = true);

        Assert.ThrowsAsync<Exception>(async () => await single.ToTask());
        Assert.That(terminated, Is.True);
    }
}
