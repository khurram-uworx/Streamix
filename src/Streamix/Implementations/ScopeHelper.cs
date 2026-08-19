using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Streamix.Implementations;

static class ScopeHelper
{
    public static async IAsyncEnumerable<T> ReadAllSupervisedAsync<T>(
        ChannelReader<T> reader,
        FluxScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            bool hasMore = false;
            try
            {
                hasMore = await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!hasMore) break;

            while (reader.TryRead(out var item))
                yield return item;

            if (scope.IsFaulted) break;
        }

        // If the scope has faulted, re-throw the first exception.
        // This ensures exceptions propagate even when the async iterator
        // has already completed normally (the .NET runtime would otherwise
        // swallow exceptions thrown in finally blocks of completed iterators).
        if (scope.IsFaulted)
        {
            scope.ThrowIfFailed();
        }
    }

    public static async Task FinalizeScopeAsync(FluxScope scope)
    {
        try
        {
            await scope.WaitAllAsync().ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            try
            {
                scope.ThrowIfFailed();
            }
            catch (OperationCanceledException)
            {
                // Expected if the scope was cancelled via external token or fail-fast
            }
        }
    }
}
