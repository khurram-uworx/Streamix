using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Streamix;

/// <summary>
/// Provides time-series extension methods for <see cref="IStream{T}"/>.
/// </summary>
public static class TimeseriesExtension
{
    /// <summary>
    /// Groups elements of a stream into windows based on their timestamps.
    /// Supports both tumbling and sliding windows.
    /// </summary>
    /// <typeparam name="T">The type of items in the stream.</typeparam>
    /// <param name="source">The source stream of timestamped items.</param>
    /// <param name="duration">The duration of each window.</param>
    /// <param name="slide">The interval at which windows are started. If null, tumbling windows are used (slide = duration).</param>
    /// <param name="capacity">The capacity of the buffer for each window.</param>
    /// <param name="mode">The backpressure mode for each window.</param>
    /// <returns>A stream of window streams.</returns>
    public static IStream<IStream<Timestamped<T>>> WindowByTime<T>(
        this IStream<Timestamped<T>> source,
        TimeSpan duration,
        TimeSpan? slide = null,
        int capacity = 16,
        ChannelBackpressureMode mode = ChannelBackpressureMode.Wait)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (slide.HasValue && slide.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(slide));

        return Stream.Create<IStream<Timestamped<T>>>(async emitter =>
        {
            var ct = emitter.CancellationToken;
            if (slide == null || slide == duration)
            {
                // Tumbling window logic
                Channel<Timestamped<T>>? currentWindowChannel = null;
                DateTimeOffset? currentWindowEnd = null;

                try
                {
                    await foreach (var item in source.WithCancellation(ct))
                    {
                        if (currentWindowChannel == null || item.Timestamp >= currentWindowEnd)
                        {
                            // Complete previous window
                            currentWindowChannel?.Writer.TryComplete();

                            // Start new window aligned to duration (using UTC ticks for consistency)
                            var startTicks = (item.Timestamp.UtcTicks / duration.Ticks) * duration.Ticks;
                            var start = new DateTimeOffset(startTicks, TimeSpan.Zero);
                            currentWindowEnd = start + duration;

                            currentWindowChannel = ChannelExecution.CreateChannel<Timestamped<T>>(capacity, mode, singleWriter: true);

                            // Emit the window stream eagerly.
                            // We use CancellationToken.None here because the window stream completion
                            // is controlled by the currentWindowChannel.Writer.TryComplete() call.
                            // This prevents TaskCanceledException when the outer stream completes.
                            await emitter.EmitAsync(Stream.From(currentWindowChannel.Reader.ReadAllAsync(CancellationToken.None)));
                        }

                        // Write the item to the current window
                        await ChannelExecution.WriteAsync(currentWindowChannel!.Writer, item, mode, CancellationToken.None);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Outer stream was cancelled, stop processing.
                }
                catch (Exception ex)
                {
                    currentWindowChannel?.Writer.TryComplete(ex);
                    throw;
                }
                finally
                {
                    currentWindowChannel?.Writer.TryComplete();
                }
            }
            else
            {
                // Sliding window logic (Task 3)
                throw new NotImplementedException("Sliding windows are not yet implemented.");
            }
        });
    }
}
