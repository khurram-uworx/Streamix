using NUnit.Framework;

namespace Streamix.Tests;

[TestFixture]
public class StructuredConcurrencyTests
{
    [Test]
    public async Task ScopedAsync_WaitsForAllTasks()
    {
        var task1Done = false;
        var task2Done = false;

        await Stream.ScopedAsync(async scope =>
        {
            scope.Run(async ct =>
            {
                await Task.Delay(50, ct);
                task1Done = true;
            });

            scope.Run(async ct =>
            {
                await Task.Delay(100, ct);
                task2Done = true;
            });
        });

        Assert.That(task1Done, Is.True);
        Assert.That(task2Done, Is.True);
    }

    [Test]
    public async Task ScopedAsync_FailFast_CancelsSiblings()
    {
        var siblingCancelled = false;
        var siblingFinished = false;

        async Task MainAction()
        {
            await Stream.ScopedAsync(async scope =>
            {
                scope.Run(async ct =>
                {
                    await Task.Delay(10, ct);
                    throw new InvalidOperationException("First failure");
                });

                scope.Run(async ct =>
                {
                    try
                    {
                        await Task.Delay(1000, ct);
                        siblingFinished = true;
                    }
                    catch (OperationCanceledException)
                    {
                        siblingCancelled = true;
                        throw;
                    }
                });
            });
        }

        Assert.ThrowsAsync<InvalidOperationException>(MainAction);
        Assert.That(siblingCancelled, Is.True);
        Assert.That(siblingFinished, Is.False);
    }

    [Test]
    public async Task ScopedAsync_OuterCancellation_CancelsChildren()
    {
        var childStarted = new TaskCompletionSource();
        var childCancelled = false;
        using var cts = new CancellationTokenSource();

        var scopedTask = Stream.ScopedAsync(async scope =>
        {
            scope.Run(async ct =>
            {
                childStarted.SetResult();
                try
                {
                    await Task.Delay(10000, ct);
                }
                catch (OperationCanceledException)
                {
                    childCancelled = true;
                    throw;
                }
            });

            await childStarted.Task;
            await Task.Delay(10000, scope.CancellationToken);
        }, cts.Token);

        await childStarted.Task;
        await Task.Delay(10);
        await cts.CancelAsync();

        Assert.CatchAsync<OperationCanceledException>(() => scopedTask);
        Assert.That(childCancelled, Is.True);
    }

    [Test]
    public async Task ScopedAsync_MainActionFailure_CancelsChildren()
    {
        var childCancelled = false;

        async Task Action() =>
            await Stream.ScopedAsync(async scope =>
            {
                scope.Run(async ct =>
                {
                    try
                    {
                        await Task.Delay(1000, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        childCancelled = true;
                        throw;
                    }
                });

                await Task.Delay(10);
                throw new InvalidOperationException("Main failure");
            });

        Assert.CatchAsync<InvalidOperationException>(Action);
        Assert.That(childCancelled, Is.True);
    }

    [Test]
    public async Task ScopedAsync_SupportsNesting()
    {
        var innerTaskDone = false;
        var outerTaskDone = false;

        await Stream.ScopedAsync(async outerScope =>
        {
            outerScope.Run(async ct =>
            {
                await Stream.ScopedAsync(async innerScope =>
                {
                    innerScope.Run(async innerCt =>
                    {
                        await Task.Delay(50, innerCt);
                        innerTaskDone = true;
                    });
                }, ct);
                outerTaskDone = true;
            });
        });

        Assert.That(innerTaskDone, Is.True);
        Assert.That(outerTaskDone, Is.True);
    }
}
