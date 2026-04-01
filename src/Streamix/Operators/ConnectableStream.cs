using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Streamix.Abstractions;

namespace Streamix.Operators;

/// <summary>
/// Implementation of <see cref="IConnectableStream{T}"/> that allows multicasting a single source to multiple subscribers.
/// </summary>
/// <typeparam name="T">The type of items in the stream.</typeparam>
internal sealed class ConnectableStream<T> : IConnectableStream<T>
{
    private readonly IStream<T> _source;
    private readonly ConcurrentDictionary<Guid, Channel<T>> _subscribers = new();
    private readonly object _lock = new();
    private int _refCount = 0;
    private CancellationTokenSource? _cts;
    private Task? _connectionTask;
    private IDisposable? _autoConnection;

    public ConnectableStream(IStream<T> source)
    {
        _source = source;
    }

    public IDisposable Connect()
    {
        lock (_lock)
        {
            if (_connectionTask != null && !_connectionTask.IsCompleted)
            {
                return new ConnectionDisposable(this);
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _connectionTask = RunConnectionInternal(token);
            return new ConnectionDisposable(this);
        }
    }

    private async Task RunConnectionInternal(CancellationToken token)
    {
        await Task.Yield();
        await RunConnection(token);
    }

    private async Task RunConnection(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _source.WithCancellation(cancellationToken))
            {
                var subscribers = _subscribers.Values.ToArray();

                foreach (var subscriber in subscribers)
                {
                    try
                    {
                        await subscriber.Writer.WriteAsync(item, cancellationToken);
                    }
                    catch { }
                }
            }

            var finalSubscribers = _subscribers.Values.ToArray();
            foreach (var subscriber in finalSubscribers)
            {
                subscriber.Writer.TryComplete();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var finalSubscribers = _subscribers.Values.ToArray();
            foreach (var subscriber in finalSubscribers)
            {
                subscriber.Writer.TryComplete();
            }
        }
        catch (Exception ex)
        {
            var finalSubscribers = _subscribers.Values.ToArray();
            foreach (var subscriber in finalSubscribers)
            {
                subscriber.Writer.TryComplete(ex);
            }
        }
        finally
        {
            lock (_lock)
            {
                _cts?.Dispose();
                _cts = null;
                _connectionTask = null;
            }
        }
    }

    public IStream<T> RefCount()
    {
        return Stream.From(RefCountInternal());
    }

    private async IAsyncEnumerable<T> RefCountInternal([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? enumerator = null;

        lock (_lock)
        {
            _refCount++;
            if (_refCount == 1)
            {
                _autoConnection = Connect();
            }
            enumerator = this.GetAsyncEnumerator(cancellationToken);
        }

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            lock (_lock)
            {
                _refCount--;
                if (_refCount == 0)
                {
                    _autoConnection?.Dispose();
                    _autoConnection = null;
                }
            }
        }
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<T>();
        _subscribers.TryAdd(id, channel);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }

            // Check for error
            await channel.Reader.Completion;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    public IStream<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        return Stream.From(MapInternal(selector));
    }

    private async IAsyncEnumerable<TResult> MapInternal<TResult>(Func<T, TResult> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            yield return selector(item);
        }
    }

    public IStream<TResult> Select<TResult>(Func<T, TResult> selector) => Map(selector);

    public IStream<T> Filter(Func<T, bool> predicate)
    {
        return Stream.From(FilterInternal(predicate));
    }

    private async IAsyncEnumerable<T> FilterInternal(Func<T, bool> predicate, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }

    public IStream<T> Where(Func<T, bool> predicate) => Filter(predicate);

    public IStream<TResult> FlatMap<TResult>(Func<T, ISingle<TResult>> selector, int maxConcurrency = 1)
    {
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than 0.");
        return maxConcurrency == 1
            ? Stream.From(FlatMapInternal(selector))
            : Stream.From(FlatMapManyConcurrentInternal(selector, maxConcurrency));
    }

    private async IAsyncEnumerable<TResult> FlatMapInternal<TResult>(Func<T, ISingle<TResult>> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            await foreach (var innerItem in selector(item).WithCancellation(cancellationToken))
            {
                yield return innerItem;
            }
        }
    }

    private async IAsyncEnumerable<TResult> FlatMapManyConcurrentInternal<TResult>(Func<T, ISingle<TResult>> selector, int maxConcurrency, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task<List<TResult>>>();

        try
        {
            await foreach (var item in this.WithCancellation(cancellationToken))
            {
                await semaphore.WaitAsync(cancellationToken);
                var task = ProcessItemAsync(item, selector, semaphore, cts.Token);
                tasks.Add(task);
            }

            var results = await Task.WhenAll(tasks);
            foreach (var list in results)
            {
                foreach (var result in list)
                {
                    yield return result;
                }
            }
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private async Task<List<TResult>> ProcessItemAsync<TResult>(T item, Func<T, ISingle<TResult>> selector, System.Threading.SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<TResult>();
            await foreach (var result in selector(item).WithCancellation(cancellationToken))
            {
                results.Add(result);
            }
            return results;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public IStream<TResult> FlatMap<TResult>(Func<T, Task<TResult>> selector, int maxConcurrency = 1)
    {
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than 0.");
        return Stream.From(FlatMapConcurrentInternal(selector, maxConcurrency));
    }

    private async IAsyncEnumerable<TResult> FlatMapConcurrentInternal<TResult>(Func<T, Task<TResult>> selector, int maxConcurrency, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (maxConcurrency == 1)
        {
            await foreach (var item in this.WithCancellation(cancellationToken))
            {
                yield return await selector(item);
            }
        }
        else
        {
            var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task<TResult>>();

            try
            {
                await foreach (var item in this.WithCancellation(cancellationToken))
                {
                    await semaphore.WaitAsync(cancellationToken);
                    tasks.Add(ExecuteSelectorAsync(item, selector, semaphore, cancellationToken));
                }

                var results = await Task.WhenAll(tasks);
                foreach (var result in results)
                {
                    yield return result;
                }
            }
            finally
            {
                semaphore.Dispose();
            }
        }
    }

    private async Task<TResult> ExecuteSelectorAsync<TResult>(T item, Func<T, Task<TResult>> selector, System.Threading.SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        try
        {
            return await selector(item);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public IStream<TResult> SelectMany<TResult>(Func<T, ISingle<TResult>> selector, int maxConcurrency = 1) => FlatMap(selector, maxConcurrency);

    public IStream<TResult> FlatMapMany<TResult>(Func<T, IStream<TResult>> selector, int maxConcurrency = 1)
    {
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than 0.");
        return maxConcurrency == 1
            ? Stream.From(FlatMapManyInternal(selector))
            : Stream.From(FlatMapManyConcurrentManyInternal(selector, maxConcurrency));
    }

    private async IAsyncEnumerable<TResult> FlatMapManyInternal<TResult>(Func<T, IStream<TResult>> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            await foreach (var innerItem in selector(item).WithCancellation(cancellationToken))
            {
                yield return innerItem;
            }
        }
    }

    private async IAsyncEnumerable<TResult> FlatMapManyConcurrentManyInternal<TResult>(Func<T, IStream<TResult>> selector, int maxConcurrency, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task<List<TResult>>>();

        try
        {
            await foreach (var item in this.WithCancellation(cancellationToken))
            {
                await semaphore.WaitAsync(cancellationToken);
                var task = ProcessItemManyAsync(item, selector, semaphore, cts.Token);
                tasks.Add(task);
            }

            var results = await Task.WhenAll(tasks);
            foreach (var list in results)
            {
                foreach (var result in list)
                {
                    yield return result;
                }
            }
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private async Task<List<TResult>> ProcessItemManyAsync<TResult>(T item, Func<T, IStream<TResult>> selector, System.Threading.SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<TResult>();
            await foreach (var result in selector(item).WithCancellation(cancellationToken))
            {
                results.Add(result);
            }
            return results;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public IStream<T> Take(int count)
    {
        return Stream.From(TakeInternal(count));
    }

    private async IAsyncEnumerable<T> TakeInternal(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var remaining = count;
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            if (remaining <= 0) yield break;
            yield return item;
            remaining--;
        }
    }

    public IStream<T> Skip(int count)
    {
        return Stream.From(SkipInternal(count));
    }

    private async IAsyncEnumerable<T> SkipInternal(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var remaining = count;
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            if (remaining > 0)
            {
                remaining--;
                continue;
            }
            yield return item;
        }
    }

    public IStream<T> MergeWith(params IStream<T>[] others)
    {
        return Stream.From(MergeWithInternal(others));
    }

    private async IAsyncEnumerable<T> MergeWithInternal(IStream<T>[] others, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var streams = new IAsyncEnumerable<T>[others.Length + 1];
        streams[0] = this;
        Array.Copy(others, 0, streams, 1, others.Length);

        foreach (var stream in streams)
        {
            await foreach (var item in stream.WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }

    public IStream<TResult> ZipWith<TOther, TResult>(IStream<TOther> other, Func<T, TOther, TResult> resultSelector)
    {
        return Stream.From(ZipWithInternal(other, resultSelector));
    }

    private async IAsyncEnumerable<TResult> ZipWithInternal<TOther, TResult>(IStream<TOther> other, Func<T, TOther, TResult> resultSelector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator1 = this.GetAsyncEnumerator(cancellationToken);
        var enumerator2 = other.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (await enumerator1.MoveNextAsync() && await enumerator2.MoveNextAsync())
            {
                yield return resultSelector(enumerator1.Current, enumerator2.Current);
            }
        }
        finally
        {
            await enumerator1.DisposeAsync();
            await enumerator2.DisposeAsync();
        }
    }

    public IStream<IList<T>> Buffer(int count)
    {
        return Stream.From(BufferInternal(count));
    }

    private async IAsyncEnumerable<IList<T>> BufferInternal(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new List<T>();
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            buffer.Add(item);
            if (buffer.Count >= count)
            {
                yield return buffer;
                buffer = new List<T>();
            }
        }
        if (buffer.Count > 0)
        {
            yield return buffer;
        }
    }

    public IStream<IStream<T>> Window(int count)
    {
        return Stream.From(WindowInternal(count));
    }

    private async IAsyncEnumerable<IStream<T>> WindowInternal(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

    public IStream<T> Throttle(TimeSpan interval)
    {
        return Stream.From(ThrottleInternal(interval));
    }

    private async IAsyncEnumerable<T> ThrottleInternal(TimeSpan interval, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastEmit = DateTime.UtcNow;
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            var now = DateTime.UtcNow;
            var timeSinceLastEmit = now - lastEmit;
            if (timeSinceLastEmit < interval)
            {
                await Task.Delay(interval - timeSinceLastEmit, cancellationToken);
            }
            yield return item;
            lastEmit = DateTime.UtcNow;
        }
    }

    public IStream<T> Delay(TimeSpan interval)
    {
        return Stream.From(DelayInternal(interval));
    }

    private async IAsyncEnumerable<T> DelayInternal(TimeSpan interval, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            await Task.Delay(interval, cancellationToken);
            yield return item;
        }
    }

    public IStream<T> Retry(int retryCount = 1)
    {
        return Stream.From(RetryInternal(retryCount));
    }

    private async IAsyncEnumerable<T> RetryInternal(int retryCount, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int attempts = 0;
        while (true)
        {
            bool failed = false;
            IAsyncEnumerator<T>? enumerator = null;
            try
            {
                try
                {
                    enumerator = this.GetAsyncEnumerator(cancellationToken);
                }
                catch (Exception)
                {
                    if (attempts >= retryCount) throw;
                    attempts++;
                    continue;
                }

                await using (enumerator)
                {
                    while (true)
                    {
                        T current = default!;
                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync();
                            if (hasNext) current = enumerator.Current;
                        }
                        catch (Exception)
                        {
                            if (attempts >= retryCount) throw;
                            attempts++;
                            failed = true;
                            break;
                        }

                        if (hasNext)
                        {
                            yield return current;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
            }

            if (!failed) yield break;
        }
    }

    public IStream<T> Timeout(TimeSpan interval)
    {
        return Stream.From(TimeoutInternal(interval));
    }

    private async IAsyncEnumerable<T> TimeoutInternal(TimeSpan interval, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = this.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            var moveNextTask = enumerator.MoveNextAsync().AsTask();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutTask = Task.Delay(interval, timeoutCts.Token);

            try
            {
                var completedTask = await Task.WhenAny(moveNextTask, timeoutTask);
                await timeoutCts.CancelAsync();

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"The operation has timed out after {interval}.");
                }

                if (await moveNextTask)
                {
                    yield return enumerator.Current;
                }
                else
                {
                    break;
                }
            }
            finally
            {
                await timeoutCts.CancelAsync();
            }
        }
    }

    public IStream<T> OnErrorResume(Func<Exception, IStream<T>> errorHandler)
    {
        return Stream.From(OnErrorResumeInternal(errorHandler));
    }

    private async IAsyncEnumerable<T> OnErrorResumeInternal(Func<Exception, IStream<T>> errorHandler, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? enumerator = null;
        IStream<T>? resumeSource = null;
        try
        {
            try
            {
                enumerator = this.GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                resumeSource = errorHandler(ex);
            }

            if (enumerator != null)
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (Exception ex)
                    {
                        resumeSource = errorHandler(ex);
                        break;
                    }

                    if (hasNext)
                    {
                        yield return enumerator.Current;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            if (enumerator != null)
            {
                await enumerator.DisposeAsync();
            }
        }

        if (resumeSource != null)
        {
            await foreach (var item in resumeSource.WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }

    public IStream<T> OnErrorReturn(T value)
    {
        return Stream.From(OnErrorReturnInternal(value));
    }

    private async IAsyncEnumerable<T> OnErrorReturnInternal(T value, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? enumerator = null;
        Exception? caughtException = null;
        try
        {
            try
            {
                enumerator = this.GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            if (enumerator != null)
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (Exception ex)
                    {
                        caughtException = ex;
                        break;
                    }

                    if (hasNext)
                    {
                        yield return enumerator.Current;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            if (enumerator != null)
            {
                await enumerator.DisposeAsync();
            }
        }

        if (caughtException != null)
        {
            yield return value;
        }
    }

    public IStream<T> OnErrorMap(Func<Exception, Exception> mapper)
    {
        return Stream.From(OnErrorMapInternal(mapper));
    }

    private async IAsyncEnumerable<T> OnErrorMapInternal(Func<Exception, Exception> mapper, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? enumerator = null;
        Exception? mappedException = null;
        try
        {
            try
            {
                enumerator = this.GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                mappedException = mapper(ex);
            }

            if (enumerator != null)
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (Exception ex)
                    {
                        mappedException = mapper(ex);
                        break;
                    }

                    if (hasNext)
                    {
                        yield return enumerator.Current;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            if (enumerator != null)
            {
                await enumerator.DisposeAsync();
            }
        }

        if (mappedException != null)
        {
            throw mappedException;
        }
    }

    public IConnectableStream<T> Publish() => this;

    public IStream<T> RunOn(TaskScheduler scheduler)
    {
        return Stream.From(RunOnInternal(scheduler));
    }

    private async IAsyncEnumerable<T> RunOnInternal(TaskScheduler scheduler, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

    public Task ForEachAsync(Action<T> action, CancellationToken cancellationToken = default)
    {
        return ForEachAsyncInternal(action, cancellationToken);
    }

    private async Task ForEachAsyncInternal(Action<T> action, CancellationToken cancellationToken)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            action(item);
        }
    }

    public Task ForEachAsync(Func<T, Task> action, CancellationToken cancellationToken = default)
    {
        return ForEachAsyncInternalAsync(action, cancellationToken);
    }

    private async Task ForEachAsyncInternalAsync(Func<T, Task> action, CancellationToken cancellationToken)
    {
        await foreach (var item in this.WithCancellation(cancellationToken))
        {
            await action(item);
        }
    }

    private void Disconnect()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
    }

    private class ConnectionDisposable : IDisposable
    {
        private readonly ConnectableStream<T> _stream;
        public ConnectionDisposable(ConnectableStream<T> stream) => _stream = stream;
        public void Dispose() => _stream.Disconnect();
    }
}
