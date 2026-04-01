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

    public IStream<TResult> Map<TResult>(Func<T, TResult> selector) => Stream.From(this).Map(selector);
    public IStream<TResult> Select<TResult>(Func<T, TResult> selector) => Map(selector);
    public IStream<T> Filter(Func<T, bool> predicate) => Stream.From(this).Filter(predicate);
    public IStream<T> Where(Func<T, bool> predicate) => Filter(predicate);
    public IStream<TResult> FlatMap<TResult>(Func<T, ISingle<TResult>> selector, int maxConcurrency = 1) => Stream.From(this).FlatMap(selector, maxConcurrency);
    public IStream<TResult> FlatMap<TResult>(Func<T, Task<TResult>> selector, int maxConcurrency = 1) => Stream.From(this).FlatMap(selector, maxConcurrency);
    public IStream<TResult> SelectMany<TResult>(Func<T, ISingle<TResult>> selector, int maxConcurrency = 1) => FlatMap(selector, maxConcurrency);
    public IStream<TResult> FlatMapMany<TResult>(Func<T, IStream<TResult>> selector, int maxConcurrency = 1) => Stream.From(this).FlatMapMany(selector, maxConcurrency);
    public IStream<T> Take(int count) => Stream.From(this).Take(count);
    public IStream<T> Skip(int count) => Stream.From(this).Skip(count);
    public IStream<T> MergeWith(params IStream<T>[] others) => Stream.From(this).MergeWith(others);
    public IStream<TResult> ZipWith<TOther, TResult>(IStream<TOther> other, Func<T, TOther, TResult> resultSelector) => Stream.From(this).ZipWith(other, resultSelector);
    public IStream<IList<T>> Buffer(int count) => Stream.From(this).Buffer(count);
    public IStream<IStream<T>> Window(int count) => Stream.From(this).Window(count);
    public IStream<T> Throttle(TimeSpan interval) => Stream.From(this).Throttle(interval);
    public IStream<T> Delay(TimeSpan interval) => Stream.From(this).Delay(interval);
    public IStream<T> Retry(int retryCount = 1) => Stream.From(this).Retry(retryCount);
    public IStream<T> Timeout(TimeSpan interval) => Stream.From(this).Timeout(interval);
    public IStream<T> OnErrorResume(Func<Exception, IStream<T>> errorHandler) => Stream.From(this).OnErrorResume(errorHandler);
    public IConnectableStream<T> Publish() => this;
    public IStream<T> RunOn(TaskScheduler scheduler) => Stream.From(this).RunOn(scheduler);
    public Task ForEachAsync(Action<T> action, CancellationToken cancellationToken = default) => Stream.From(this).ForEachAsync(action, cancellationToken);
    public Task ForEachAsync(Func<T, Task> action, CancellationToken cancellationToken = default) => Stream.From(this).ForEachAsync(action, cancellationToken);

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
