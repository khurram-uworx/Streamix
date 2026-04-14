using System.Collections.Concurrent;

namespace Streamix.Implementations;

internal sealed class StreamScope : IStreamScope, IAsyncDisposable
{
    readonly CancellationTokenSource cts;
    readonly ConcurrentBag<Task> tasks = new();
    readonly object syncRoot = new();
    bool disposed;

    public StreamScope(CancellationToken externalToken)
    {
        this.cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
    }

    public CancellationToken CancellationToken => cts.Token;

    public void Run(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        lock (syncRoot)
        {
            if (disposed) throw new ObjectDisposedException(nameof(StreamScope));

            // If already cancelled, we still want to track the task but it will likely exit immediately
            var task = Task.Run(async () =>
            {
                try
                {
                    await work(cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    // Expected
                }
                catch (Exception)
                {
                    await cts.CancelAsync();
                    throw;
                }
            }, cts.Token);

            tasks.Add(task);
        }
    }

    public async Task WaitAllAsync()
    {
        while (true)
        {
            Task[] toWait;
            lock (syncRoot)
            {
                toWait = tasks.Where(t => !t.IsCompleted).ToArray();
            }

            if (toWait.Length == 0) break;

            try
            {
                await Task.WhenAll(toWait);
            }
            catch
            {
                // We don't catch here to propagate first failure,
                // but we need to make sure we wait for all before finishing ScopedAsync.
                // Actually, Task.WhenAll will throw if any fails.
                // But we want to ensure other tasks are cancelled and waited upon.
                break;
            }
        }

        // Final wait for everything to settle (including cancellations)
        List<Task> allTasks;
        lock (syncRoot)
        {
            allTasks = tasks.ToList();
        }

        if (allTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(allTasks);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // If there's any non-cancellation exception, throw that instead
                var firstFaulted = allTasks.FirstOrDefault(t => t.IsFaulted);
                if (firstFaulted?.Exception != null)
                {
                    throw firstFaulted.Exception.InnerException ?? firstFaulted.Exception;
                }
                throw;
            }
            catch
            {
                // Propagate exception after all have settled
                var firstFaulted = allTasks.FirstOrDefault(t => t.IsFaulted);
                if (firstFaulted?.Exception != null)
                {
                    throw firstFaulted.Exception.InnerException ?? firstFaulted.Exception;
                }
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (syncRoot)
        {
            if (disposed) return;
            disposed = true;
        }
        await cts.CancelAsync();
        cts.Dispose();
    }
}
