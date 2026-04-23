# Streamix Architecture

This document holds the design intent and implementation-oriented semantics for Streamix.

## Core Model

Modern .NET has `IAsyncEnumerable<T>` and channels, but Streamix intentionally provides a unified composable abstraction on top:

- `Stream<T>` for 0..N item streams
- `Single<T>` for 0..1 item streams

`Single.From(...)` supports values, `Task<T>`, `ValueTask<T>`, and `IAsyncEnumerable<T>` sources.

The default mental model is:

- cold, pull-based streams built on `IAsyncEnumerable<T>`
- channels only when coordination or fan-out is needed
- explicit async composition, cancellation, ordering, and error propagation

## Ordering and Concurrency Semantics

Ordered operators have explicit runtime semantics:

- `MapOrdered` and `FlatMapOrdered` preserve source order even when later work finishes first.
- Later ordered results or failures are not observed until earlier ordered work has been drained.
- `FlatMapOrdered` may buffer later inner items up to `maxBufferedItemsPerInner` while waiting for earlier inners.
- Cancelling enumeration stops waiting and propagates cancellation into the ordered operator's in-flight work.

## Structured Concurrency and Supervision

Streamix uses one supervision and lifetime model across explicit scopes, concurrent operators, and channel-backed execution boundaries, but these concepts remain distinct in the public API.

- **Bounded Operator Concurrency**: Operator settings such as `maxConcurrency` are throughput controls. They limit how many asynchronous operations may run at once inside an operator, but they are not themselves a separate structured-concurrency entry point.
- **Explicit Supervision Boundaries**: `Stream.ScopedAsync` defines a clear parent-child lifetime boundary for concurrent work created inside the scope body.
- **Deterministic Settlement**: A supervision boundary does not complete until the parent body has finished AND all registered child tasks have reached a terminal state.
- **Fail-Fast**: The first observed non-cancellation fault triggers boundary-wide cancellation via a linked token.
- **Exception Propagation**: After all children settle, the first encountered non-cancellation exception is propagated. Subsequent faults are suppressed to ensure deterministic error handling.
- **Boundary Integration**: Channel-backed boundaries (`PipeThroughChannel`, `RunOnChannel`) participate in the same cancellation, failure, and settlement model, ensuring that worker tasks are properly tracked and settled without changing the stream-first mental model.
- **Side-Channel Integration**: `TeeToChannel` remains a stream operator rather than a terminal. It mirrors items into a caller-supplied channel while leaving the main stream contract, supervision, and downstream composition model intact.

## Concurrency Verification Matrix

The following matrix is the public audit view of the current concurrency contract. It points to representative tests rather than exhaustively listing every operator-specific assertion.

| Invariant | Representative coverage | What it demonstrates |
|----------|----------|----------|
| Success and deterministic settlement | `StreamScopeTests.ScopedAsync_WaitsForAllChildren`, `StreamScopeTests.ScopedAsync_NestedScopes_ComposeSupervision`, `StreamTests.PipeThroughChannel_SupervisesProducer_AndWaitsForSettlement`, `StreamTests.RunOnChannel_SupervisesWorkers_AndWaitsForSettlement` | Explicit scopes and channel-backed boundaries do not complete before their child work has reached a terminal state. |
| Cancellation propagation | `StreamScopeTests.ScopedAsync_ParentCancellationCancelsChildren`, `ConcurrencyTests.FlatMapOrdered_StopsPromptlyWhenConsumerCancels`, `ResourceSafetyTests.FlatMap_CancelsOutstandingTasks_OnCancellation`, `ResourceSafetyTests.MapOrdered_CancelsOutstandingTasks_OnCancellation` | Outer cancellation flows into supervised child work and stops ordered/concurrent operators promptly. |
| First-fault propagation | `StreamScopeTests.ScopedAsync_PropagatesFirstException`, `ConcurrencyTests.FlatMap_PropagatesFirstException`, `ConcurrencyTests.MapOrdered_PropagatesErrorsCorrectly`, `ConcurrencyTests.FlatMapOrdered_PropagatesFailurePromptly` | The first non-cancellation fault wins and is the exception observed by the caller after required settlement. |
| Sibling cancellation and fail-fast behavior | `StreamScopeTests.ScopedAsync_ChildFailureCancelsSiblings`, `ConcurrencyTests.FlatMap_SiblingCancellationOnFailure`, `ConcurrencyTests.FlatMapOrdered_InnerFailure_CancelsSiblings_AndWaitsForSettlement` | A fault in one child cancels sibling work, and the boundary still waits for sibling teardown before exiting. |
| Ordering guarantees | `ConcurrencyTests.FlatMap_OrderingIsNonDeterministic`, `ConcurrencyTests.MapOrdered_PreservesOrder`, `ConcurrencyTests.MapOrdered_DefersLaterFailureUntilEarlierWorkCanDrain`, `StreamTests.RunOnChannel_PreservesOrdering` | Unordered operators may emit out of order, while ordered operators and `RunOnChannel(...)` preserve source order and defer later failures until earlier work can drain. |
| Bounded concurrency and backpressure | `ConcurrencyTests.FlatMap_RespectsMaxConcurrency`, `ConcurrencyTests.FlatMapOrdered_RespectsMaxConcurrency`, `ConcurrencyTests.MapOrdered_RespectsMaxConcurrency`, `ConcurrencyTests.FlatMap_BackpressureBlocksProducer`, `ConcurrencyTests.FlatMapOrdered_BoundsBufferedItemsPerInner`, `StreamTests.ToChannel_Supports_Backpressure`, `StreamTests.PipeThroughChannel_Fail_ThrowsWhenBoundaryIsFull` | `maxConcurrency` is enforced as throughput control, internal buffering remains bounded, and channel boundaries keep backpressure observable rather than hiding it behind unbounded queues. |
| Teardown and resource safety | `ResourceSafetyTests.Merge_DisposesAllSources_OnCancellation`, `ResourceSafetyTests.Merge_DisposesAllSources_OnFailure`, `ResourceSafetyTests.Using_IDisposable_DisposesOnCancellation`, `ResourceSafetyTests.Using_IAsyncDisposable_DisposesOnCancellation`, `ResourceSafetyTests.SupervisedBoundary_DisposesResources_OnlyAfterAllChildrenSettle`, `StreamTests.ToChannel_Completes_Writer_With_Upstream_Error`, `StreamTests.TeeToChannel_LeavesWriterOpen_ByDefault`, `StreamTests.TeeToChannel_CompletesWriterWithError_WhenRequested` | Resource disposal, sink/channel completion, and supervised teardown remain aligned with completion, failure, and cancellation semantics. |

Audit outcome:

- No release-blocking invariant gap was found in the audited concurrency suites.
- The matrix is intentionally organized by invariant rather than by file so new concurrent operators can be mapped to an existing contract row.
- Channel-boundary documentation is now aligned with the verified runtime contract, including completion and teardown ownership for `TeeToChannel(...)`.

## Design Principles

- Async-first (`IAsyncEnumerable<T>` + Channels)
- Small, .NET-idiomatic API surface
- Minimal, .NET-idiomatic operators
- Pull by default, channel-backed coordination when needed
- Explicit behavior around concurrency, cancellation, and error propagation
- Optional interop with AsyncRx.NET through a separate package
- Optional EF integration through `Streamix.Extensions` (not in core `Streamix`)

## Extension Package Boundaries

- `Streamix` (core) has no `Microsoft.EntityFrameworkCore` dependency.
- `Streamix.Extensions` hosts optional integrations with heavier transitive graphs (for example AsyncRx and EF Core).
- Consumers that only need core stream operators should reference `Streamix` directly.

## Entity Framework Stream Semantics

`EfStream` in `Streamix.Extensions` adapts EF queries to `IStream<T>` with explicit lifetime and materialization behavior:

- Public buffered entry points are `EfStream.From(...)` and `Func<DbContext>.ToStream(...)`.
- Public streamed entry points are `EfStream.FromStreamed(...)` and `Func<DbContext>.ToStreamed(...)`.
- Factory-based usage creates one `DbContext` per subscription and disposes it on completion, error, or cancellation.
- Query builder delegates are executed against the same context instance that performs query execution.
- Buffered execution uses `ToListAsync(cancellationToken)`, then emits each item to downstream operators.
- Streamed execution uses `AsAsyncEnumerable()` and emits items as EF async enumeration advances.
- Buffered materialization remains the default on existing APIs for backward-compatible semantics.
- Streamed execution can short-circuit earlier under downstream operators such as `Take`, but it keeps the `DbContext` alive for the duration of enumeration.
- Streamed execution is provider-sensitive: if ordering matters, it must be expressed in the query; cancellation may be observed at different points by different providers; and failures can surface after partial emission rather than strictly before the first item.
- Caller-owned `DbContext` overloads are intentionally excluded so that subscription ownership, disposal, and same-context execution rules stay explicit and safe.
- EF-specific batching or paging helpers are intentionally deferred until real usage shows a gap that the buffered-versus-streamed choice plus existing Streamix operators does not address well.

## Implementation Notes

- Channels for flow control
- Lightweight operator chaining
- No reflection or heavy runtime magic
- Fully compatible with async streams

## Performance Guardrails and Characteristics

Streamix is designed for high-performance asynchronous streaming with the following characteristics:

- Backpressure by Design: Concurrent operators like `FlatMap`, `FlatMapOrdered`, `Merge`, and the task-returning concurrent `Map` overload utilize bounded `System.Threading.Channels`. This ensures that if a consumer is slower than the producer, the producer is naturally paused once the internal buffers are full, preventing unbounded memory growth.
- Zero-Allocation Sequential Operators: Basic operators like `Map`, `Filter`, `Take`, and `Skip` are implemented as thin wrappers over `IAsyncEnumerable<T>` using async iterators. They introduce minimal overhead and do not involve intermediate buffering.
- Bounded Concurrency: All flattening and parallel operators accept a `maxConcurrency` parameter, allowing you to strictly control the number of simultaneous asynchronous operations. This is a throughput setting, not a separate supervision contract.
- Materialization Awareness: Operators that require state across multiple items, such as `Buffer(count)`, `Window(count)`, or `Replay(bufferSize)`, involve allocations proportional to their requested size. These should be used with appropriate bounds to manage memory usage.
- Watermark-Aware Windowing: Supports bounded out-of-order data processing by deriving a monotonic watermark (`maxObservedEventTimestamp - outOfOrderness`). Late events (timestamp <= watermark) are dropped, and windows are finalized once the watermark reaches the window's end.
- Hot Stream Efficiency: `ConnectableStream<T>` (via `Publish()` or `Replay()`) manages a single underlying subscription for multiple downstream consumers, reducing redundant upstream work and resource consumption.

## Time-Based Operator Semantics

Current time-based support is intentionally narrow:

- Event-time operators: `MapWithTimestamp`, `WindowByTime`, and `WindowBySession`
- Processing-time operators: `Throttle`, `BufferByTime`, and `Sample`

`BufferByTime(interval, maxCount)` uses a bounded internal coordination path so downstream slowness still propagates pressure back toward the source rather than being hidden behind an unbounded queue.

- A buffer is emitted when the interval elapses and the current buffer is non-empty.
- A buffer is emitted early when `maxCount` is reached.
- On successful upstream completion, a trailing non-empty buffer is emitted once, then the operator completes.
- On upstream failure, no synthetic final buffer is emitted; the error is propagated.
- On cancellation, partial buffered state is discarded and the operator does not complete successfully.

`Sample(interval)` also uses bounded coordination and emits at most one value per interval.

- Each tick emits the most recently observed item since the previous tick, if any.
- Intervals with no observed items emit nothing.
- On successful upstream completion, the latest observed item is emitted once if one is pending, then the operator completes.
- On upstream failure, no final sampled item is emitted; the error is propagated.
- On cancellation, pending latest state is discarded and the operator does not complete successfully.

These processing-time operators are intentionally wall-clock based through the stream clock and are separate from event-time semantics such as timestamps, watermarks, lateness, and session formation.

## Channel-Boundary Semantics

Channel APIs are explicit execution and interop boundaries inside a stream-first model. They do not replace `IStream<T>` as the main composition surface.

- `ToChannel(...)` is a terminal adapter that writes the stream into a channel.
- `PipeThroughChannel(...)` inserts a channel-backed execution boundary and returns a new stream.
- `RunOnChannel(...)` inserts a channel-backed execution boundary with a worker relay while preserving source order.
- `TeeToChannel(...)` mirrors items into an existing side channel and still returns the original stream items downstream.

Completion and teardown ownership are intentionally explicit:

- `PipeThroughChannel(...)` and `RunOnChannel(...)` own their internal channel boundary and do not complete successfully until the boundary's producer or worker tasks have settled.
- `TeeToChannel(...)` does not make the side channel the owner of the pipeline. By default the supplied writer stays open after the stream completes because the caller owns that channel lifetime.
- `TeeToChannel(..., completeWriter: true)` transfers completion ownership for the mirrored writer to the operator, so successful completion and upstream failure are forwarded to that side channel as well.
- `TeeToChannel(...)` is not a replacement for `ToChannel(...)`: use the tee when you want mirrored fan-out plus continued stream composition, and use `ToChannel(...)` when the channel is the terminal destination.

## Boundary Semantics

- `ToDictionaryAsync(...)` follows .NET `Dictionary` semantics and throws on duplicate keys.
- `ToLookupAsync(...)` materializes grouped output and supports comparer overloads for key handling.
- `ContainsAsync(...)` short-circuits as soon as a matching value is found.
- `MinByAsync(...)` and `MaxByAsync(...)` support comparer overloads when default key ordering is not the desired ordering.
- `DrainAsync(...)` is the explicit completion-only terminal when you care about completion, cancellation, or failure but not emitted items.

Sink completion semantics are explicit:

- on successful completion, `CompleteAsync(null)` is called when completion is owned by the terminal
- on upstream or sink write failure, `CompleteAsync(error)` is called and the original exception is still propagated to the caller
- on cancellation, Streamix stops writing and does not complete the sink

`ToChannel(...)` is implemented as an adapter over the same sink path, so channel writes and custom sinks follow the same completion and error rules.
