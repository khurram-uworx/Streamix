namespace Streamix;

/// <summary>
/// Provides resilience-oriented convenience operators for common retry and recovery patterns.
/// </summary>
public static class ResilienceExtensions
{
    /// <summary>
    /// Resumes a stream with a single asynchronously computed value if an error occurs.
    /// </summary>
    public static IFlux<T> OnErrorReturnAsync<T>(
        this IFlux<T> source,
        Func<Exception, CancellationToken, ValueTask<T>> errorHandler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(errorHandler);

        return source.OnErrorResume(ex => Flux.From(Flux.FromValueTask(ct => errorHandler(ex, ct))));
    }

    /// <summary>
    /// Retries a stream and returns a fallback value if all retry attempts fail.
    /// </summary>
    public static IFlux<T> RetryThenReturn<T>(
        this IFlux<T> source,
        int retryCount,
        Func<Exception, T> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fallbackFactory);

        return source.Retry(retryCount).OnErrorReturn(fallbackFactory);
    }

    /// <summary>
    /// Retries a stream and returns an asynchronously computed fallback value if all retry attempts fail.
    /// </summary>
    public static IFlux<T> RetryThenReturnAsync<T>(
        this IFlux<T> source,
        int retryCount,
        Func<Exception, CancellationToken, ValueTask<T>> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fallbackFactory);

        return source.Retry(retryCount).OnErrorReturnAsync(fallbackFactory);
    }

    /// <summary>
    /// Retries a stream and resumes with a fallback stream if all retry attempts fail.
    /// </summary>
    public static IFlux<T> RetryThenResume<T>(
        this IFlux<T> source,
        int retryCount,
        Func<Exception, IFlux<T>> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fallbackFactory);

        return source.Retry(retryCount).OnErrorResume(fallbackFactory);
    }

    /// <summary>
    /// Resumes a single-item stream with an asynchronously computed value if an error occurs.
    /// </summary>
    public static ISingle<T> OnErrorReturnAsync<T>(
        this ISingle<T> source,
        Func<Exception, CancellationToken, ValueTask<T>> errorHandler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(errorHandler);

        return source.OnErrorResume(ex => Single.FromValueTask(ct => errorHandler(ex, ct)));
    }

    /// <summary>
    /// Retries a single-item stream and returns a fallback value if all retry attempts fail.
    /// </summary>
    public static ISingle<T> RetryThenReturn<T>(
        this ISingle<T> source,
        int retryCount,
        Func<Exception, T> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fallbackFactory);

        return source.Retry(retryCount).OnErrorReturn(fallbackFactory);
    }

    /// <summary>
    /// Retries a single-item stream and returns an asynchronously computed fallback value if all retry attempts fail.
    /// </summary>
    public static ISingle<T> RetryThenReturnAsync<T>(
        this ISingle<T> source,
        int retryCount,
        Func<Exception, CancellationToken, ValueTask<T>> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fallbackFactory);

        return source.Retry(retryCount).OnErrorReturnAsync(fallbackFactory);
    }

    /// <summary>
    /// Retries a single-item stream and resumes with a fallback single if all retry attempts fail.
    /// </summary>
    public static ISingle<T> RetryThenResume<T>(
        this ISingle<T> source,
        int retryCount,
        Func<Exception, ISingle<T>> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fallbackFactory);

        return source.Retry(retryCount).OnErrorResume(fallbackFactory);
    }
}
