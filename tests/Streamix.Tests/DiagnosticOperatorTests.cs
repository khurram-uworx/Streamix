using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Diagnostics;

namespace Streamix.Tests;

[TestFixture]
public class DiagnosticOperatorTests
{
    private static readonly object consoleCaptureLock = new();
#if DEBUG
    private static readonly object debugCaptureLock = new();
#endif

    [Test]
    public async Task Stream_DoOnNext_ExecutesForEveryItem()
    {
        var items = new List<int>();
        var result = await Flux.Range(1, 5)
            .DoOnNext(x => items.Add(x))
            .Select(x => x)
            .ToListAsync();

        Assert.That(items, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task Stream_Do_ExecutesForEveryItem()
    {
        var items = new List<int>();
        var result = await Flux.Range(1, 5)
            .Do(x => items.Add(x))
            .ToListAsync();

        Assert.That(items, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task Stream_Tap_ExecutesForEveryItem()
    {
        var items = new List<int>();
        var result = await Flux.Range(1, 5)
            .Tap(x => items.Add(x))
            .ToListAsync();

        Assert.That(items, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void Stream_DoOnError_ExecutesUponStreamFailure()
    {
        var exception = new Exception("Test error");
        Exception? caught = null;

        var stream = Flux.Error<int>(exception)
            .DoOnError(ex => caught = ex);

        Assert.ThrowsAsync<Exception>(async () => await stream.ToListAsync());
        Assert.That(caught, Is.SameAs(exception));
    }

    [Test]
    public async Task Stream_DoOnTerminate_ExecutesUponSuccessfulCompletion()
    {
        bool terminated = false;
        await Flux.Range(1, 3)
            .DoOnTerminate(() => terminated = true)
            .ToListAsync();

        Assert.That(terminated, Is.True);
    }

    [Test]
    public async Task Stream_DoOnComplete_ExecutesUponSuccessfulCompletion()
    {
        bool completed = false;
        await Flux.Range(1, 3)
            .DoOnComplete(() => completed = true)
            .ToListAsync();

        Assert.That(completed, Is.True);
    }

    [Test]
    public void Stream_DoOnComplete_DoesNotExecuteUponError()
    {
        bool completed = false;
        var stream = Flux.Error<int>(new Exception())
            .DoOnComplete(() => completed = true);

        Assert.ThrowsAsync<Exception>(async () => await stream.ToListAsync());
        Assert.That(completed, Is.False);
    }

    [Test]
    public async Task Stream_DoOnComplete_DoesNotExecuteUponCancellation()
    {
        bool completed = false;
        using var cts = new CancellationTokenSource();

        var stream = Flux.Range(1, 10)
            .DoOnComplete(() => completed = true);

        try
        {
            await foreach (var item in stream.WithCancellation(cts.Token))
            {
                if (item == 5) cts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(completed, Is.False);
    }

    [Test]
    public void Stream_DoOnTerminate_ExecutesUponError()
    {
        bool terminated = false;
        var stream = Flux.Error<int>(new Exception())
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
    public async Task Single_Do_ExecutesForItem()
    {
        int value = 0;
        var result = await Single.From(42)
            .Do(x => value = x)
            .ToTask();

        Assert.That(value, Is.EqualTo(42));
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Single_Tap_ExecutesForItem()
    {
        int value = 0;
        var result = await Single.From(42)
            .Tap(x => value = x)
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
    public async Task Single_DoOnComplete_ExecutesUponSuccessfulCompletion()
    {
        bool completed = false;
        await Single.From(42)
            .DoOnComplete(() => completed = true)
            .ToTask();

        Assert.That(completed, Is.True);
    }

    [Test]
    public void Single_DoOnComplete_DoesNotExecuteUponError()
    {
        bool completed = false;
        var single = Single.Error<int>(new Exception())
            .DoOnComplete(() => completed = true);

        Assert.ThrowsAsync<Exception>(async () => await single.ToTask());
        Assert.That(completed, Is.False);
    }

    [Test]
    public async Task Single_DoOnComplete_DoesNotExecuteUponCancellation()
    {
        bool completed = false;
        using var cts = new CancellationTokenSource();

        // Use a task that respects cancellation
        var single = Single.From(Task.Delay(1000, cts.Token).ContinueWith(_ => 42, cts.Token))
            .DoOnComplete(() => completed = true);

        var task = single.ToTask(cts.Token);
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await task);
        Assert.That(completed, Is.False);
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

    [Test]
    public async Task Stream_Log_Action_LogsAllSignals()
    {
        var logs = new List<string>();
        await Flux.Range(1, 2)
            .Named("TestStream")
            .LogAction(s => logs.Add(s))
            .DrainAsync();

        Assert.That(logs, Contains.Item("[TestStream] Next(1)"));
        Assert.That(logs, Contains.Item("[TestStream] Next(2)"));
        Assert.That(logs, Contains.Item("[TestStream] Completed"));
    }

    [Test]
    public async Task Stream_Log_Action_Prefix_LogsAllSignals()
    {
        var logs = new List<string>();
        await Flux.Range(1, 2)
            .LogAction(s => logs.Add(s))
            .DrainAsync();

        Assert.That(logs, Contains.Item("Next(1)"));
        Assert.That(logs, Contains.Item("Next(2)"));
        Assert.That(logs, Contains.Item("Completed"));
    }

    [Test]
    public void Stream_Log_Action_LogsError()
    {
        var logs = new List<string>();
        var stream = Flux.Error<int>(new Exception("Fail"))
            .Named("ErrorStream")
            .LogAction(s => logs.Add(s));

        Assert.ThrowsAsync<Exception>(async () => await stream.DrainAsync());
        Assert.That(logs, Contains.Item("[ErrorStream] Error(Fail)"));
    }

    [Test]
    public async Task Stream_Log_ILogger_LogsAllSignals()
    {
        var mockLogger = new Mock<ILogger>();
        await Flux.Range(1, 1)
            .Named("LoggerStream")
            .Log(mockLogger.Object)
            .DrainAsync();

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[LoggerStream] Next(1)")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[LoggerStream] Completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task Single_Log_Action_LogsAllSignals()
    {
        var logs = new List<string>();
        await Single.From(42)
            .Named("TestSingle")
            .LogAction(s => logs.Add(s))
            .ToTask();

        Assert.That(logs, Contains.Item("[TestSingle] Next(42)"));
        Assert.That(logs, Contains.Item("[TestSingle] Completed"));
    }

    [Test]
    public async Task Stream_Log_StringPrefix_LogsAllSignals()
    {
        var output = await CaptureConsoleOutAsync(async () =>
        {
            await Flux.Range(1, 2)
                .Log("PrefixStream")
                .DrainAsync();
        });

        Assert.That(output, Does.Contain("[PrefixStream] Next(1)"));
        Assert.That(output, Does.Contain("[PrefixStream] Next(2)"));
        Assert.That(output, Does.Contain("[PrefixStream] Completed"));
    }

    [Test]
    public async Task Single_Log_StringPrefix_LogsAllSignals()
    {
        var output = await CaptureConsoleOutAsync(async () =>
        {
            await Single.From(42)
                .Log("PrefixSingle")
                .ToTask();
        });

        Assert.That(output, Does.Contain("[PrefixSingle] Next(42)"));
        Assert.That(output, Does.Contain("[PrefixSingle] Completed"));
    }

    [Test]
    public async Task Stream_Checkpoint_LogsTimingInformation()
    {
        var logs = new List<string>();
        await Flux.Range(1, 2)
            .Checkpoint("TestCheckpoint", s => logs.Add(s))
            .DrainAsync();

        Assert.That(logs.Any(l => l.Contains("[Checkpoint: TestCheckpoint] Next(1)") && l.Contains("Total:") && l.Contains("Since last:")), Is.True);
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: TestCheckpoint] Next(2)") && l.Contains("Total:") && l.Contains("Since last:")), Is.True);
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: TestCheckpoint] Completed") && l.Contains("Total:")), Is.True);
    }

    [Test]
    public void Stream_Checkpoint_LogsErrorTiming()
    {
        var logs = new List<string>();
        var stream = Flux.Error<int>(new Exception("Fail"))
            .Checkpoint("ErrorCheckpoint", s => logs.Add(s));

        Assert.ThrowsAsync<Exception>(async () => await stream.DrainAsync());
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: ErrorCheckpoint] Error(Fail)") && l.Contains("Total:")), Is.True);
    }

    [Test]
    public async Task Stream_Checkpoint_WithContext_LogsSelectedContext()
    {
        var logs = new List<string>();

        await Flux.Range(1, 2)
            .Checkpoint("ContextCheckpoint", x => $"item-{x}", s => logs.Add(s))
            .DrainAsync();

        Assert.That(logs.Any(l => l.Contains("[Checkpoint: ContextCheckpoint] Next(item-1)") && l.Contains("Total:")), Is.True);
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: ContextCheckpoint] Next(item-2)") && l.Contains("Since last:")), Is.True);
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: ContextCheckpoint] Completed")), Is.True);
    }

    [Test]
    public void Stream_Checkpoint_WithContext_IncludesLastContextOnError()
    {
        static async IAsyncEnumerable<int> Failing()
        {
            yield return 1;
            throw new InvalidOperationException("Boom");
        }

        var logs = new List<string>();
        var stream = Flux.From(Failing())
            .Checkpoint("ContextError", x => $"item-{x}", s => logs.Add(s));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await stream.DrainAsync());
        Assert.That(logs.Any(l =>
            l.Contains("[Checkpoint: ContextError] Error(Boom)") &&
            l.Contains("Context: item-1")), Is.True);
    }

    [Test]
    public void Stream_Checkpoint_WithContext_PropagatesContextSelectorFailure()
    {
        var logs = new List<string>();
        var stream = Flux.Range(1, 1)
            .Checkpoint("ContextSelector", _ => throw new InvalidOperationException("Selector"), s => logs.Add(s));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await stream.DrainAsync());
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: ContextSelector] Error(Selector)")), Is.True);
    }

    [Test]
    public async Task Single_Checkpoint_LogsTimingInformation()
    {
        var logs = new List<string>();
        await Single.From(42)
            .Checkpoint("SingleCheckpoint", s => logs.Add(s))
            .ToTask();

        Assert.That(logs.Any(l => l.Contains("[Checkpoint: SingleCheckpoint] Next(42)") && l.Contains("Total:")), Is.True);
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: SingleCheckpoint] Completed") && l.Contains("Total:")), Is.True);
    }

    [Test]
    public async Task Single_Checkpoint_LogsCancellationTiming()
    {
        var logs = new List<string>();
        using var cts = new CancellationTokenSource();

        var single = Single.From(Task.Delay(1000, cts.Token).ContinueWith(_ => 42, cts.Token))
            .Checkpoint("CancelledSingle", s => logs.Add(s));

        var task = single.ToTask(cts.Token);
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await task);
        Assert.That(logs.Any(l => l.Contains("[Checkpoint: CancelledSingle] Cancelled") && l.Contains("Total:")), Is.True);
    }

    [Test]
    public async Task Stream_Trace_LogsAllLifecycleSignals()
    {
        var logs = new List<string>();
        await Flux.Range(1, 2)
            .Named("TraceStream")
            .TraceAction(s => logs.Add(s))
            .DrainAsync();

        Assert.That(logs[0], Is.EqualTo("[TraceStream] Subscribe"));
        Assert.That(logs[1], Is.EqualTo("[TraceStream] Request(1)"));
        Assert.That(logs[2], Is.EqualTo("[TraceStream] Next(1)"));
        Assert.That(logs[3], Is.EqualTo("[TraceStream] Request(1)"));
        Assert.That(logs[4], Is.EqualTo("[TraceStream] Next(2)"));
        Assert.That(logs[5], Is.EqualTo("[TraceStream] Request(1)"));
        Assert.That(logs[6], Is.EqualTo("[TraceStream] Completed"));
        Assert.That(logs[7], Is.EqualTo("[TraceStream] Dispose"));
    }

    [Test]
    public void Stream_Trace_LogsErrorSignal()
    {
        var logs = new List<string>();
        var stream = Flux.Error<int>(new Exception("TraceFail"))
            .Named("ErrorTrace")
            .TraceAction(s => logs.Add(s));

        Assert.ThrowsAsync<Exception>(async () => await stream.DrainAsync());

        Assert.That(logs, Contains.Item("[ErrorTrace] Subscribe"));
        Assert.That(logs, Contains.Item("[ErrorTrace] Error(TraceFail)"));
        Assert.That(logs, Contains.Item("[ErrorTrace] Dispose"));
    }

    [Test]
    public async Task Stream_Trace_StringPrefix_LogsAllLifecycleSignals()
    {
        var output = await CaptureConsoleOutAsync(async () =>
        {
            await Flux.Range(1, 1)
                .Trace("TracePrefix")
                .DrainAsync();
        });

        Assert.That(output, Does.Contain("[TracePrefix] Subscribe"));
        Assert.That(output, Does.Contain("[TracePrefix] Request(1)"));
        Assert.That(output, Does.Contain("[TracePrefix] Next(1)"));
        Assert.That(output, Does.Contain("[TracePrefix] Completed"));
        Assert.That(output, Does.Contain("[TracePrefix] Dispose"));
    }

    [Test]
    public async Task Single_Trace_StringPrefix_LogsAllLifecycleSignals()
    {
        var output = await CaptureConsoleOutAsync(async () =>
        {
            await Single.From(42)
                .Trace("TraceSinglePrefix")
                .ToTask();
        });

        Assert.That(output, Does.Contain("[TraceSinglePrefix] Subscribe"));
        Assert.That(output, Does.Contain("[TraceSinglePrefix] Request(1)"));
        Assert.That(output, Does.Contain("[TraceSinglePrefix] Next(42)"));
        Assert.That(output, Does.Contain("[TraceSinglePrefix] Completed"));
        Assert.That(output, Does.Contain("[TraceSinglePrefix] Dispose"));
    }

    [Test]
    public async Task Single_Trace_LogsAllLifecycleSignals()
    {
        var logs = new List<string>();
        await Single.From(42)
            .Named("TraceSingle")
            .TraceAction(s => logs.Add(s))
            .ToTask();

        Assert.That(logs, Contains.Item("[TraceSingle] Subscribe"));
        Assert.That(logs, Contains.Item("[TraceSingle] Request(1)"));
        Assert.That(logs, Contains.Item("[TraceSingle] Next(42)"));
        Assert.That(logs, Contains.Item("[TraceSingle] Completed"));
        Assert.That(logs, Contains.Item("[TraceSingle] Dispose"));
    }

    [Test]
    public async Task Stream_Trace_ILogger_Prefix_LogsAllLifecycleSignals()
    {
        var mockLogger = new Mock<ILogger>();

        await Flux.Range(1, 1)
            .Trace(mockLogger.Object, "TraceLoggerPrefix")
            .DrainAsync();

        VerifyInfoLog(mockLogger, "[TraceLoggerPrefix] Subscribe", Times.Once());
        VerifyInfoLog(mockLogger, "[TraceLoggerPrefix] Request(1)", Times.Exactly(2));
        VerifyInfoLog(mockLogger, "[TraceLoggerPrefix] Next(1)", Times.Once());
        VerifyInfoLog(mockLogger, "[TraceLoggerPrefix] Completed", Times.Once());
        VerifyInfoLog(mockLogger, "[TraceLoggerPrefix] Dispose", Times.Once());
    }

    [Test]
    public async Task Single_Trace_ILogger_Prefix_LogsAllLifecycleSignals()
    {
        var mockLogger = new Mock<ILogger>();

        await Single.From(42)
            .Trace(mockLogger.Object, "SingleTraceLoggerPrefix")
            .ToTask();

        VerifyInfoLog(mockLogger, "[SingleTraceLoggerPrefix] Subscribe", Times.Once());
        VerifyInfoLog(mockLogger, "[SingleTraceLoggerPrefix] Request(1)", Times.AtLeastOnce());
        VerifyInfoLog(mockLogger, "[SingleTraceLoggerPrefix] Next(42)", Times.Once());
        VerifyInfoLog(mockLogger, "[SingleTraceLoggerPrefix] Completed", Times.Once());
        VerifyInfoLog(mockLogger, "[SingleTraceLoggerPrefix] Dispose", Times.Once());
    }

    [Test]
    public async Task Stream_Trace_LogsCancellation()
    {
        var logs = new List<string>();
        using var cts = new CancellationTokenSource();
        var stream = Flux.Interval(TimeSpan.FromMilliseconds(10))
            .TraceAction(s => logs.Add(s));

        var task = Task.Run(async () =>
        {
            await foreach (var item in stream.WithCancellation(cts.Token))
            {
                if (item == 2) cts.Cancel();
            }
        });

        Assert.CatchAsync<OperationCanceledException>(async () => await task);

        Assert.That(logs, Contains.Item("Subscribe"));
        Assert.That(logs, Contains.Item("Cancelled"));
        Assert.That(logs, Contains.Item("Dispose"));
    }

    [Test]
    public async Task Stream_Debug_StringPrefix_WritesPrefixedSignals()
    {
#if DEBUG
        var output = await CaptureDebugOutAsync(async () =>
        {
            await Flux.Range(1, 2)
                .Debug("DebugPrefix")
                .DrainAsync();
        });

        Assert.That(output, Does.Contain("[DebugPrefix] Next(1)"));
        Assert.That(output, Does.Contain("[DebugPrefix] Next(2)"));
        Assert.That(output, Does.Contain("[DebugPrefix] Completed"));
#else
        var result = await Flux.Range(1, 2)
            .Debug("DebugPrefix")
            .ToListAsync();

        Assert.That(result, Is.EqualTo(new[] { 1, 2 }));
#endif
    }

    [Test]
    public async Task Single_Debug_StringPrefix_WritesPrefixedSignals()
    {
#if DEBUG
        var output = await CaptureDebugOutAsync(async () =>
        {
            await Single.From(42)
                .Debug("DebugSinglePrefix")
                .ToTask();
        });

        Assert.That(output, Does.Contain("[DebugSinglePrefix] Next(42)"));
        Assert.That(output, Does.Contain("[DebugSinglePrefix] Completed"));
#else
        var result = await Single.From(42)
            .Debug("DebugSinglePrefix")
            .ToTask();

        Assert.That(result, Is.EqualTo(42));
#endif
    }

    private static void VerifyInfoLog(Mock<ILogger> mockLogger, string message, Times times)
    {
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    private static Task<string> CaptureConsoleOutAsync(Func<Task> action)
    {
        lock (consoleCaptureLock)
        {
            return Task.FromResult(CaptureConsoleOutCoreAsync(action).GetAwaiter().GetResult());
        }
    }

    private static async Task<string> CaptureConsoleOutCoreAsync(Func<Task> action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await action();
            await writer.FlushAsync();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

#if DEBUG
    private static Task<string> CaptureDebugOutAsync(Func<Task> action)
    {
        lock (debugCaptureLock)
        {
            return Task.FromResult(CaptureDebugOutCoreAsync(action).GetAwaiter().GetResult());
        }
    }

    private static async Task<string> CaptureDebugOutCoreAsync(Func<Task> action)
    {
        using var writer = new StringWriter();
        using var listener = new TextWriterTraceListener(writer);
        Trace.Listeners.Add(listener);

        try
        {
            await action();
            Trace.Flush();
            await writer.FlushAsync();
            return writer.ToString();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }
#endif

    [Test]
    public async Task Stream_DoOnNextAsync_Task_ExecutesForEveryItem()
    {
        var items = new List<int>();
        Task OnNextAsync(int x) { items.Add(x); return Task.CompletedTask; }
        var result = await Flux.Range(1, 5)
            .DoOnNextAsync(OnNextAsync)
            .ToListAsync();

        Assert.That(items, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task Stream_DoOnNextAsync_ValueTask_ExecutesForEveryItem()
    {
        var items = new List<int>();
        ValueTask OnNextAsync(int x) { items.Add(x); return ValueTask.CompletedTask; }
        var result = await Flux.Range(1, 5)
            .DoOnNextAsync(OnNextAsync)
            .ToListAsync();

        Assert.That(items, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task Single_DoOnNextAsync_Task_ExecutesForItem()
    {
        var item = 0;
        Task OnNextAsync(int x) { item = x; return Task.CompletedTask; }
        var result = await Single.From(42)
            .DoOnNextAsync(OnNextAsync)
            .ToTask();

        Assert.That(item, Is.EqualTo(42));
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Single_DoOnNextAsync_ValueTask_ExecutesForItem()
    {
        var item = 0;
        ValueTask OnNextAsync(int x) { item = x; return ValueTask.CompletedTask; }
        var result = await Single.From(42)
            .DoOnNextAsync(OnNextAsync)
            .ToTask();

        Assert.That(item, Is.EqualTo(42));
        Assert.That(result, Is.EqualTo(42));
    }
}
