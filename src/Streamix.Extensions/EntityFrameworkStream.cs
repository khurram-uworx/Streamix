using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;

namespace Streamix.Extensions;

/// <summary>
/// Implementation of <see cref="IStream{T}"/> that fetches data from Entity Framework Core.
/// This class is internal as it's intended to be created via extension methods.
/// </summary>
/// <typeparam name="T">The type of items in the stream.</typeparam>
class EntityFrameworkStream<T> : IStream<T> where T : class
{
    readonly IQueryable<T> queryable;
    readonly IClock clock;
    readonly string? name;
    readonly Func<DbContext> dbContextFactory;

    /// <summary>
    /// Creates a new EntityFrameworkStream.
    /// </summary>
    /// <param name="queryable">The EF Core queryable source.</param>
    /// <param name="dbContextFactory">Factory function to create DbContext instances.</param>
    /// <param name="clock">The clock to use for time-based operations.</param>
    /// <param name="name">The name of the stream.</param>
    public EntityFrameworkStream(IQueryable<T> queryable, Func<DbContext> dbContextFactory, IClock? clock = null, string? name = null)
    {
        this.queryable = queryable;
        this.dbContextFactory = dbContextFactory;
        this.clock = clock ?? SystemClock.Instance;
        this.name = name;
    }

    /// <inheritdoc />
    public IClock Clock => clock;

    /// <inheritdoc />
    public string? Name => name;

    /// <inheritdoc />
    public IStream<T> Named(string name)
    {
        return new EntityFrameworkStream<T>(queryable, dbContextFactory, clock, name);
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

    /// <inheritdoc />
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return executeQuery(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    async IAsyncEnumerable<T> executeQuery([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var dbContext = dbContextFactory();
        
        // Materialize the query to avoid issues with context disposal
        var results = await queryable.ToListAsync(cancellationToken);
        
        foreach (var item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
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
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0.");
        return Stream.From(window(count), clock, name);
    }

    /// <inheritdoc />
    public IStream<T> RunOn(TaskScheduler scheduler)
    {
        return Stream.From(runOn(scheduler), clock, name);
    }
}
