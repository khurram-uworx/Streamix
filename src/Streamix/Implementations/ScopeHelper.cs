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
            // A faulted scope must surface its error through MoveNextAsync, which is the
            // only reliable error path for IAsyncEnumerable. Relying on the iterator's
            // finally / DisposeAsync to throw is unsafe: those exceptions are routinely
            // swallowed by consumers, operators, and the runtime. That swallow was the
            // root cause of #155 (BackpressureException silently lost for
            // Flux.From(IEnumerable) sources).
            if (scope.IsFaulted)
                scope.ThrowIfFailed();

            bool hasMore;
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
                // Best-effort secondary surfacing for cases where the consumer stopped
                // enumerating before the fault was observed. No-op if already surfaced
                // (ThrowIfFailed clears the recorded exception after throwing).
                scope.ThrowIfFailed();
            }
            catch (OperationCanceledException)
            {
                // Expected if the scope was cancelled via external token or fail-fast
            }
        }
    }
}
