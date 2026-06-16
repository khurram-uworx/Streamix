using NUnit.Framework;

namespace Streamix.Tests;

[TestFixture]
public class ErrorHandlingTests
{
    [Test]
    public async Task Stream_OnErrorResume_Recovers_From_Error()
    {
        var exception = new InvalidOperationException("Initial failure");
        IFlux<int> stream = Flux.Error<int>(exception)
            .OnErrorResume(ex =>
            {
                Assert.That(ex, Is.SameAs(exception));
                return Flux.Range(1, 3);
            });

        var result = new List<int>();
        await foreach (var item in stream)
        {
            result.Add(item);
        }

        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Stream_OnErrorResume_MidStream_Recovers()
    {
        async IAsyncEnumerable<int> FailingSource()
        {
            yield return 1;
            yield return 2;
            throw new Exception("Boom");
        }

        IFlux<int> stream = Flux.From(FailingSource())
            .OnErrorResume(ex => Flux.From(100));

        var result = new List<int>();
        await foreach (var item in stream)
        {
            result.Add(item);
        }

        Assert.That(result, Is.EqualTo(new[] { 1, 2, 100 }));
    }

    [Test]
    public void Stream_OnErrorResume_Propagates_Recovery_Failure()
    {
        var recoveryException = new Exception("Recovery failed");
        IFlux<int> stream = Flux.Error<int>(new Exception("Initial"))
            .OnErrorResume(ex => throw recoveryException);

        Assert.ThrowsAsync<Exception>(async () =>
        {
            await foreach (var _ in stream) { }
        }, "Recovery failed");
    }

    [Test]
    public async Task Stream_OnErrorReturn_Returns_Value()
    {
        IFlux<int> stream = Flux.Error<int>(new Exception("Fail"))
            .OnErrorReturn(42);

        var result = new List<int>();
        await foreach (var item in stream) result.Add(item);

        Assert.That(result, Is.EqualTo(new[] { 42 }));
    }

    [Test]
    public void Stream_OnErrorMap_Transforms_Exception()
    {
        IFlux<int> stream = Flux.Error<int>(new InvalidOperationException("Original"))
            .OnErrorMap(ex => new ArgumentException("Mapped", ex));

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in stream) { }
        });
        Assert.That(ex.Message, Is.EqualTo("Mapped"));
        Assert.That(ex.InnerException, Is.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task Stream_ErrorOperators_NoOp_When_No_Error()
    {
        IFlux<int> stream = Flux.Range(1, 3)
            .OnErrorResume(ex => Flux.From(10))
            .OnErrorReturn(20)
            .OnErrorMap(ex => new Exception("Should not happen"));

        var result = new List<int>();
        await foreach (var item in stream) result.Add(item);

        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Stream_OnErrorResume_Respects_Cancellation()
    {
        var cts = new CancellationTokenSource();
        IFlux<int> stream = Flux.Error<int>(new Exception("Fail"))
            .OnErrorResume(ex => Flux.Range(1, 100));

        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in stream.WithCancellation(cts.Token))
            {
            }
        });
    }

    [Test]
    public async Task Single_OnErrorResume_Recovers()
    {
        ISingle<int> single = Single.Error<int>(new Exception("Fail"))
            .OnErrorResume(ex => Single.From(42));

        int result = await single.ToTask();
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Single_OnErrorReturn_Returns_Value()
    {
        ISingle<int> single = Single.Error<int>(new Exception("Fail"))
            .OnErrorReturn(42);

        int result = await single.ToTask();
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void Single_OnErrorMap_Transforms_Exception()
    {
        ISingle<int> single = Single.Error<int>(new InvalidOperationException("Original"))
            .OnErrorMap(ex => new ArgumentException("Mapped", ex));

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await single.ToTask();
        });
        Assert.That(ex.Message, Is.EqualTo("Mapped"));
    }

    [Test]
    public async Task Single_ErrorOperators_NoOp_When_No_Error()
    {
        ISingle<int> single = Single.From(1)
            .OnErrorResume(ex => Single.From(10))
            .OnErrorReturn(20)
            .OnErrorMap(ex => new Exception("Should not happen"));

        int result = await single.ToTask();
        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public async Task Single_OnErrorReturn_Func_ReturnsValueFromException()
    {
        ISingle<int> single = Single.Error<int>(new InvalidOperationException("Fail"))
            .OnErrorReturn(ex =>
            {
                Assert.That(ex, Is.InstanceOf<InvalidOperationException>());
                Assert.That(ex.Message, Is.EqualTo("Fail"));
                return 42;
            });

        int result = await single.ToTask();
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Stream_OnErrorReturn_Func_ReturnsValueFromException()
    {
        IFlux<int> stream = Flux.Error<int>(new InvalidOperationException("Fail"))
            .OnErrorReturn(ex =>
            {
                Assert.That(ex, Is.InstanceOf<InvalidOperationException>());
                Assert.That(ex.Message, Is.EqualTo("Fail"));
                return 42;
            });

        var result = await stream.ToListAsync();
        Assert.That(result, Is.EqualTo(new[] { 42 }));
    }

    [Test]
    public async Task Stream_OnErrorReturn_Func_MidStream_Recovers()
    {
        async IAsyncEnumerable<int> FailingSource()
        {
            yield return 1;
            yield return 2;
            throw new Exception("Boom");
        }

        IFlux<int> stream = Flux.From(FailingSource())
            .OnErrorReturn(ex => 99);

        var result = await stream.ToListAsync();
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 99 }));
    }

    [Test]
    public async Task Stream_OnErrorReturnAsync_ReturnsValueFromException()
    {
        IFlux<int> stream = Flux.Error<int>(new InvalidOperationException("Fail"))
            .OnErrorReturnAsync((ex, ct) =>
            {
                Assert.That(ex, Is.InstanceOf<InvalidOperationException>());
                Assert.That(ct.IsCancellationRequested, Is.False);
                return ValueTask.FromResult(42);
            });

        var result = await stream.ToListAsync();
        Assert.That(result, Is.EqualTo(new[] { 42 }));
    }

    [Test]
    public void Stream_OnErrorReturnAsync_PropagatesFallbackCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(10);

        IFlux<int> stream = Flux.Error<int>(new InvalidOperationException("Fail"))
            .OnErrorReturnAsync(async (_, ct) =>
            {
                await Task.Delay(1000, ct);
                return 42;
            });

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await stream.ToListAsync(cts.Token));
    }

    [Test]
    public async Task Stream_RetryThenReturn_RetriesBeforeFallback()
    {
        var attempts = 0;
        IFlux<int> stream = Flux.Defer(() =>
        {
            attempts++;
            return attempts < 3
                ? Flux.Error<int>(new InvalidOperationException($"Fail {attempts}"))
                : Flux.Just(42);
        }).RetryThenReturn(3, ex => -1);

        var result = await stream.ToListAsync();

        Assert.That(result, Is.EqualTo(new[] { 42 }));
        Assert.That(attempts, Is.EqualTo(3));
    }

    [Test]
    public async Task Stream_RetryThenReturn_UsesFallbackAfterExhaustion()
    {
        var attempts = 0;
        Exception? seen = null;
        IFlux<int> stream = Flux.Defer(() =>
        {
            attempts++;
            return Flux.Error<int>(new InvalidOperationException($"Fail {attempts}"));
        }).RetryThenReturn(2, ex =>
        {
            seen = ex;
            return -1;
        });

        var result = await stream.ToListAsync();

        Assert.That(result, Is.EqualTo(new[] { -1 }));
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(seen?.Message, Is.EqualTo("Fail 3"));
    }

    [Test]
    public async Task Stream_RetryThenReturnAsync_UsesAsyncFallbackAfterExhaustion()
    {
        var attempts = 0;
        IFlux<int> stream = Flux.Defer(() =>
        {
            attempts++;
            return Flux.Error<int>(new InvalidOperationException("Fail"));
        }).RetryThenReturnAsync(1, (ex, ct) => ValueTask.FromResult(-1));

        var result = await stream.ToListAsync();

        Assert.That(result, Is.EqualTo(new[] { -1 }));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task Stream_RetryThenResume_UsesFallbackStreamAfterExhaustion()
    {
        IFlux<int> stream = Flux.Error<int>(new InvalidOperationException("Fail"))
            .RetryThenResume(1, ex => Flux.From(7, 8));

        var result = await stream.ToListAsync();

        Assert.That(result, Is.EqualTo(new[] { 7, 8 }));
    }

    [Test]
    public async Task Single_OnErrorReturnAsync_ReturnsValueFromException()
    {
        ISingle<int> single = Single.Error<int>(new InvalidOperationException("Fail"))
            .OnErrorReturnAsync((ex, ct) => ValueTask.FromResult(42));

        var result = await single.ToTask();

        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Single_RetryThenReturn_RetriesBeforeFallback()
    {
        var attempts = 0;
        ISingle<int> single = Single.Defer(() =>
        {
            attempts++;
            return attempts < 2
                ? Single.Error<int>(new InvalidOperationException("Fail"))
                : Single.Just(42);
        }).RetryThenReturn(2, ex => -1);

        var result = await single.ToTask();

        Assert.That(result, Is.EqualTo(42));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task Single_RetryThenReturnAsync_UsesAsyncFallbackAfterExhaustion()
    {
        var attempts = 0;
        ISingle<int> single = Single.Defer(() =>
        {
            attempts++;
            return Single.Error<int>(new InvalidOperationException("Fail"));
        }).RetryThenReturnAsync(1, (ex, ct) => ValueTask.FromResult(-1));

        var result = await single.ToTask();

        Assert.That(result, Is.EqualTo(-1));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task Single_RetryThenResume_UsesFallbackSingleAfterExhaustion()
    {
        ISingle<int> single = Single.Error<int>(new InvalidOperationException("Fail"))
            .RetryThenResume(1, ex => Single.Just(7));

        var result = await single.ToTask();

        Assert.That(result, Is.EqualTo(7));
    }
}
