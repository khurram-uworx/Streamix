using NUnit.Framework;
using Streamix.Implementations;

namespace Streamix.Tests;

[TestFixture]
public class UnionOperatorTests
{
    [Test]
    public async Task FlatMap_SiblingCancellationOnFailure()
    {
        // Arrange
        var source = Stream.From(1, 2, 3);
        var cancelledSiblings = 0;

        // Act
        var stream = source.FlatMap(i => Stream.Create<int>(async (emitter, ct) =>
        {
            if (i == 1)
            {
                await Task.Delay(100); // Give others time to start
                throw new Exception("First failure");
            }

            try
            {
                await Task.Delay(5000, ct);
                await emitter.EmitAsync(i);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelledSiblings);
                throw;
            }
        }), maxConcurrency: 3);

        // Assert
        var ex = Assert.ThrowsAsync<Exception>(async () => {
            await foreach (var item in stream)
            {
                // Consume
            }
        });
        Assert.That(ex.Message, Is.EqualTo("First failure"));
        Assert.That(cancelledSiblings, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task MapOrdered_WaitsForAllChildrenToSettle()
    {
        // Arrange
        var source = Stream.From(1, 2);
        var completedChildren = 0;

        // Act
        var stream = source.MapOrdered(async i =>
        {
            if (i == 1)
            {
                throw new Exception("Failure");
            }

            await Task.Delay(200);
            Interlocked.Increment(ref completedChildren);
            return i;
        }, maxConcurrency: 2);

        // Assert
        var ex = Assert.ThrowsAsync<Exception>(async () => await stream.ForEachAsync(_ => { }));
        Assert.That(completedChildren, Is.EqualTo(1));
    }

    [Test]
    public async Task FlatMap_PropagatesFirstException()
    {
        // Arrange
        var source = Stream.From(1, 2);

        // Act
        var stream = source.FlatMap(async (int i) =>
        {
            if (i == 1)
            {
                throw new Exception("First");
            }
            await Task.Delay(100);
            throw new Exception("Second");
            return i;
        }, maxConcurrency: 2);

        // Assert
        var ex = Assert.ThrowsAsync<Exception>(async () => await stream.ForEachAsync(_ => { }));
        Assert.That(ex.Message, Is.EqualTo("First"));
    }

    [Test]
    public async Task FlatMap_StopsYieldingImmediatelyOnFault()
    {
        // Arrange
        var source = Stream.Range(1, 10);
        var yieldedItems = new List<int>();

        // Act
        var stream = source.FlatMap(async i =>
        {
            if (i == 1)
            {
                await Task.Delay(100);
                throw new Exception("Boom");
            }

            await Task.Delay(200); // Should finish after failure
            return i;
        }, maxConcurrency: 5);

        // Assert
        Assert.ThrowsAsync<Exception>(async () => await stream.ForEachAsync(i => {
            lock(yieldedItems) yieldedItems.Add(i);
        }));

        // We might yield some if they finished exactly during failure,
        // but we definitely shouldn't yield all 9 successful items.
        Assert.That(yieldedItems.Count, Is.LessThan(9));
    }
}
