using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Streamix.Implementations;

/// <summary>
/// Implementation of <see cref="IConnectableStream{T}"/> that allows multicasting a single source to multiple subscribers.
/// This class is internal as it's intended to be created via the <see cref="StreamExtensions.Publish"/> method.
/// </summary>
/// <typeparam name="T">The type of items in the stream.</typeparam>
class ConnectableStream<T> : IConnectableStream<T>
{
    class ConnectionDisposable : IDisposable
    {
        readonly ConnectableStream<T> stream;
        int disposed = 0;
        public ConnectionDisposable(ConnectableStream<T> stream) => this.stream = stream;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                stream.disconnect();
        }
    }

    readonly IStream<T> source;
    readonly IClock clock;
    readonly string? name;
    readonly ConcurrentDictionary<Guid, Channel<T>> subscribers = new();
    readonly object _lock = new();
    readonly int replayBufferSize;
    readonly Queue<T> replayBuffer = new();
    bool isCompleted;
    Exception? error;
    int refCounter = 0;
    CancellationTokenSource? cts;
    Task? connectionTask;
    IDisposable? autoConnection;
    TaskCompletionSource<bool>? refCountDisconnectedTcs;

    public ConnectableStream(IStream<T> source, int bufferSize = 0, IClock? clock = null, string? name = null)
    {
        if (bufferSize < 0) throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be non-negative.");
        this.source = source;
        this.replayBufferSize = bufferSize;
        this.clock = clock ?? (source is StreamImplementation<T> s ? s.Clock : Streamix.Implementations.SystemClock.Instance);
        this.name = name ?? source.Name;
    }

    /// <inheritdoc />
    public IClock Clock => clock;

    /// <inheritdoc />
    public string? Name => name;

    /// <inheritdoc />
    async Task runConnectionInternal(CancellationToken token)
    {
        await Task.Yield();
        await runConnection(token);
    }

    async Task runConnection(CancellationToken cancellationToken)
    {
        try
        {
            await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
            while (await enumerator.MoveNextAsync())
            {
                var item = enumerator.Current;
                Channel<T>[] currentSubscribers;

                lock (_lock)
                {
                    if (replayBufferSize > 0)
                    {
                        replayBuffer.Enqueue(item);
                        while (replayBuffer.Count > replayBufferSize)
                            replayBuffer.Dequeue();
                    }
                    currentSubscribers = this.subscribers.Values.ToArray();
                }

                foreach (var subscriber in currentSubscribers)
                {
                    try
                    {
                        await subscriber.Writer.WriteAsync(item, cancellationToken);
                    }
                    catch { }
                }
            }

            lock (_lock)
            {
                isCompleted = true;
                var finalSubscribers = subscribers.Values.ToArray();
                foreach (var subscriber in finalSubscribers)
                    subscriber.Writer.TryComplete();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_lock)
            {
                isCompleted = true;
                var finalSubscribers = subscribers.Values.ToArray();
                foreach (var subscriber in finalSubscribers)
                    subscriber.Writer.TryComplete();
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                isCompleted = true;
                error = ex;
                var finalSubscribers = subscribers.Values.ToArray();
                foreach (var subscriber in finalSubscribers)
                    subscriber.Writer.TryComplete(ex);
            }
        }
        finally
        {
            lock (_lock)
            {
                cts?.Dispose();
                cts = null;
                connectionTask = null;
            }
        }
    }

    async IAsyncEnumerable<T> refCount([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? enumerator = null;
        bool incremented = false;

        try
        {
            lock (_lock)
            {
                refCounter++;
                incremented = true;
                if (refCounter == 1)
                    autoConnection = Connect();
                enumerator = this.GetAsyncEnumerator(cancellationToken);
            }

            while (await enumerator.MoveNextAsync())
                yield return enumerator.Current;
        }
        finally
        {
            if (enumerator != null)
                await enumerator.DisposeAsync();

            if (incremented)
            {
                lock (_lock)
                {
                    refCounter--;

                    if (refCounter == 0)
                    {
                        autoConnection?.Dispose();
                        autoConnection = null;
                        refCountDisconnectedTcs?.TrySetResult(true);
                        refCountDisconnectedTcs = null;
                    }
                }
            }
        }
    }

    async IAsyncEnumerable<T> mergeWith(IStream<T>[] others, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var streams = new IAsyncEnumerable<T>[others.Length + 1];
        streams[0] = this;
        Array.Copy(others, 0, streams, 1, others.Length);

        foreach (var stream in streams)
            await foreach (var item in stream.WithCancellation(cancellationToken))
                yield return item;
    }

    async IAsyncEnumerator<T> getAsyncEnumeratorImpl(Guid id, Channel<T> channel, CancellationToken cancellationToken)
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
                while (channel.Reader.TryRead(out var item))
                    yield return item;

            // Check for error
            await channel.Reader.Completion;
        }
        finally
        {
            subscribers.TryRemove(id, out _);
        }
    }

    async IAsyncEnumerable<TResult> zipWith<TOther, TResult>(IStream<TOther> other, Func<T, TOther, TResult> resultSelector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator1 = this.GetAsyncEnumerator(cancellationToken);
        var enumerator2 = other.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (await enumerator1.MoveNextAsync() && await enumerator2.MoveNextAsync())
                yield return resultSelector(enumerator1.Current, enumerator2.Current);
        }
        finally
        {
            await enumerator1.DisposeAsync();
            await enumerator2.DisposeAsync();
        }
    }

    async IAsyncEnumerable<IStream<T>> window(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var window = new List<T>();
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            window.Add(item);
            if (window.Count >= count)
            {
                yield return Stream.From(window.ToAsyncEnumerable());
                window.RemoveAt(0);
            }
        }
    }

    async IAsyncEnumerable<T> runOn(TaskScheduler scheduler, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            yield return await Task.Factory.StartNew(
                () => item,
                cancellationToken,
                TaskCreationOptions.DenyChildAttach,
                scheduler);
        }
    }

    void disconnect()
    {
        lock (_lock)
        {
            cts?.Cancel();
        }
    }

    /// <inheritdoc />
    public IStream<T> Named(string name)
    {
        return new ConnectableStream<T>(source, replayBufferSize, clock, name);
    }

    /// <summary>
    /// Returns a task that completes when all RefCount subscribers have disconnected.
    /// This is useful for testing RefCount behavior without relying on timing assumptions.
    /// </summary>
    public Task WhenRefCountDisconnectedAsync()
    {
        lock (_lock)
        {
            if (refCounter == 0)
                return Task.CompletedTask;

            refCountDisconnectedTcs = new TaskCompletionSource<bool>();
            return refCountDisconnectedTcs.Task;
        }
    }

    /// <inheritdoc />
    public IDisposable Connect()
    {
        lock (_lock)
        {
            // Only reuse the existing connection if it's still running and hasn't completed
            // If isCompleted is true, we must start a fresh connection with reset state
            if (connectionTask != null && !connectionTask.IsCompleted && !isCompleted)
                return new ConnectionDisposable(this);

            replayBuffer.Clear();
            isCompleted = false;
            error = null;

            cts = new CancellationTokenSource();
            var token = cts.Token;
            connectionTask = runConnectionInternal(token);
            return new ConnectionDisposable(this);
        }
    }

    /// <inheritdoc />
    public IStream<T> RefCount()
    {
        return Stream.From(refCount(), clock, name);
    }

    /// <inheritdoc />
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<T>();

        lock (_lock)
        {
            if (replayBufferSize > 0)
            {
                foreach (var item in replayBuffer)
                    channel.Writer.TryWrite(item);
            }

            if (isCompleted)
            {
                channel.Writer.TryComplete(error);
            }
            else
            {
                subscribers.TryAdd(id, channel);
            }
        }

        return getAsyncEnumeratorImpl(id, channel, cancellationToken);
    }

    /// <inheritdoc />
    public IStream<T> MergeWith(params IStream<T>[] others)
    {
        return Stream.From(mergeWith(others), clock, name);
    }

    /// <inheritdoc />
    public IStream<TResult> ZipWith<TOther, TResult>(IStream<TOther> other, Func<T, TOther, TResult> resultSelector)
    {
        return Stream.From(zipWith(other, resultSelector), clock, name);
    }

    /// <inheritdoc />
    public IStream<IStream<T>> Window(int count)
    {
        return Stream.From(window(count), clock, name);
    }

    /// <inheritdoc />
    public IStream<T> RunOn(TaskScheduler scheduler)
    {
        return Stream.From(runOn(scheduler), clock, name);
    }
}
