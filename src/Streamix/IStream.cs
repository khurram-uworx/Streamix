namespace Streamix;

/// <summary>
/// Represents a stream of 0..N values, backed by <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <typeparam name="T">The type of items in the stream.</typeparam>
public interface IStream<T> : IAsyncEnumerable<T>
{
    /// <summary>
    /// </summary>
    public IClock Clock { get; }
    /// <summary>
    /// Gets the name of the stream, if any.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Sets the name of the stream.
    /// </summary>
    /// <param name="name">The name to set.</param>
    /// <returns>A new <see cref="IStream{T}"/> with the specified name.</returns>
    IStream<T> Named(string name);

    /// <summary>
    /// Merges this stream with other streams into a single stream.
    /// </summary>
    /// <param name="others">The other streams to merge with.</param>
    /// <returns>A merged <see cref="IStream{T}"/>.</returns>
    IStream<T> MergeWith(params IStream<T>[] others);

    /// <summary>
    /// Zips this stream with another stream using a result selector function.
    /// </summary>
    /// <typeparam name="TOther">The type of elements in the other stream.</typeparam>
    /// <typeparam name="TResult">The type of elements in the resulting stream.</typeparam>
    /// <param name="other">The other stream to zip with.</param>
    /// <param name="resultSelector">A function that specifies how to combine the elements from the two streams.</param>
    /// <returns>An <see cref="IStream{TResult}"/> that contains zipped elements of the two streams.</returns>
    IStream<TResult> ZipWith<TOther, TResult>(IStream<TOther> other, Func<T, TOther, TResult> resultSelector);

    /// <summary>
    /// Groups elements of a stream into windows of a specified size.
    /// </summary>
    /// <param name="count">The maximum size of each window.</param>
    /// <returns>An <see cref="IStream{T}"/> of <see cref="IStream{T}"/>.</returns>
    IStream<IStream<T>> Window(int count);

    /// <summary>
    /// Executes upstream operations (including source enumeration) on the specified scheduler.
    /// This is equivalent to SubscribeOn in other reactive libraries.
    /// </summary>
    /// <param name="scheduler">The task scheduler to run the operations on.</param>
    /// <returns>An <see cref="IStream{T}"/> scheduled on the specified scheduler.</returns>
    IStream<T> RunOn(TaskScheduler scheduler);
}
