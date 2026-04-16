# Duplication Findings: `IStream` Implementations

This document lists potential duplication between:

- `src/Streamix/Implementations/StreamImplementation.cs`
- `src/Streamix/Implementations/ConnectableStream.cs`

Scope includes methods from the `IStream<T>` contract and private helper methods they call.

## Duplicated `IStream` Contract Methods

The following interface methods are implemented in both classes with substantially similar logic:

- `Named`
- `Map(Func<T, TResult>)`
- `MapAwait(Func<T, ValueTask<TResult>>)`
- `Filter(Func<T, bool>)`
- `FilterAsync(Func<T, ValueTask<bool>>)`
- `FlatMap(Func<T, ISingle<TResult>>, int maxConcurrency = int.MaxValue)`
- `FlatMapAwait(Func<T, ValueTask<ISingle<TResult>>>, int maxConcurrency = int.MaxValue)`
- `Map(Func<T, Task<TResult>>, int maxConcurrency = int.MaxValue)`
- `MapOrdered(Func<T, Task<TResult>>, int maxConcurrency)`
- `FlatMap(Func<T, Task<TResult>>, int maxConcurrency = int.MaxValue)`
- `FlatMap(Func<T, IStream<TResult>>, int maxConcurrency = int.MaxValue)`
- `ConcatMap(Func<T, IStream<TResult>>)`
- `FlatMapOrdered(Func<T, IStream<TResult>>, int maxConcurrency = int.MaxValue, int maxBufferedItemsPerInner = 16)`
- `Take(int count)`
- `Skip(int count)`
- `Buffer(int count)`
- `Throttle(TimeSpan interval)`
- `Delay(TimeSpan interval)`
- `Retry(int retryCount = 1)`
- `Retry(int retryCount, Func<int, Exception, TimeSpan> backoffStrategy)`
- `Timeout(TimeSpan interval)`
- `RunOn(TaskScheduler scheduler)`
- `ForEachAsync(Action<T> action, CancellationToken cancellationToken = default)`
- `ForEachAsync(Func<T, Task> action, CancellationToken cancellationToken = default)`

## Duplicated Private/Helper Methods

The following helpers contain duplicated or near-duplicated logic across both implementations:

- `map`
- `mapAwait`
- `filter`
- `filterAsync`
- `flatMap`
- `parallelMapEnumerable`
- `parallelMapTask`
- `parallelMapTaskOrdered`
- `concatMapInternal`
- `flatMapOrdered`
- `flatMapAwaitConcurrent`
- `take`
- `skip`
- `buffer`
- `throttle`
- `delay`
- `retry`
- `timeout`
- `runOn`
- `forEachAsync` overloads (private in `ConnectableStream`; inline in public methods for `StreamImplementation`)

## Same Contract, Different Behavior (Not Pure Duplication)

These methods share the same interface contract but currently differ in behavior and/or implementation details:

- `MergeWith`
  - `StreamImplementation` uses a concurrent merge helper.
  - `ConnectableStream` uses a sequential `mergeWith` helper.
- `ZipWith`
  - Implemented via different helper approaches in each class.
- `Window`
  - `StreamImplementation` appears to emit fixed-size chunks plus trailing partial.
  - `ConnectableStream` appears to behave like a sliding window and may not emit trailing partial.
- `GetAsyncEnumerator`
  - Intentionally different due to connectable/multicast semantics in `ConnectableStream`.

## Notes

- This is an inventory of duplication candidates, not a behavior-equivalence guarantee.
- Any refactor to extension-method composition should preserve intended connectable stream semantics where they intentionally differ.

## Extraction Plan (Appendix)

This plan targets a minimal-interface + extension-method composition model while minimizing behavior regressions.

### Phase 1: Low Risk (Extract First)

These are mostly stateless, single-pass operators with straightforward semantics:

✅ `Map(Func<T, TResult>)`
✅ `MapAwait(Func<T, ValueTask<TResult>>)`
✅ `Filter(Func<T, bool>)`
✅ `FilterAsync(Func<T, ValueTask<bool>>)`
✅ `Take(int)`
✅ `Skip(int)`
✅ `Throttle(TimeSpan)`
✅ `Delay(TimeSpan)`
✅ `ForEachAsync(Action<T>, CancellationToken)`
✅ `ForEachAsync(Func<T, Task>, CancellationToken)`
- `Named` (if naming semantics are uniform and preserved through `Stream.From(...)`)

**Suggested approach**

- Move logic to `StreamExtensions` as compositional extension methods over the minimal primitive(s).
- Keep temporary forwarding methods on implementations (calling extensions) to avoid large interface churn in one step.
- Add/confirm tests per operator:
  - normal flow
  - cancellation propagation
  - exception propagation

### Phase 2: Medium Risk (Extract with Focused Tests)

These operators are more complex but still likely unifiable with shared helper utilities:

✅ `Map(Func<T, Task<TResult>>, int maxConcurrency)`
✅ `MapOrdered(Func<T, Task<TResult>>, int maxConcurrency)`
✅ `FlatMap(Func<T, ISingle<TResult>>, int maxConcurrency)`
✅ `FlatMapAwait(Func<T, ValueTask<ISingle<TResult>>>, int maxConcurrency)`
✅ `FlatMap(Func<T, Task<TResult>>, int maxConcurrency)`
✅ `FlatMap(Func<T, IStream<TResult>>, int maxConcurrency)`
✅ `ConcatMap(Func<T, IStream<TResult>>)`
✅ `FlatMapOrdered(Func<T, IStream<TResult>>, int maxConcurrency, int maxBufferedItemsPerInner)`
✅ `Buffer(int)`
✅ `Retry(int)`
✅ `Retry(int, Func<int, Exception, TimeSpan>)`
✅ `Timeout(TimeSpan)`

**Suggested approach**

- First extract shared private algorithms into internal static helpers (e.g., `StreamOperatorCore`) used by both implementations.
- Then migrate public surface to extension methods that call the shared helpers.
- Standardize guard clauses (`maxConcurrency`, `count`, buffer sizes) in one place.
- Add/confirm tests for:
  - ordering guarantees (`MapOrdered`, `ConcatMap`, `FlatMapOrdered`)
  - concurrency limits
  - backpressure/buffering behavior
  - retry/backoff timing and terminal failure
  - timeout behavior and cancellation race conditions

### Phase 3: High Risk / Behavior Decision Required

These currently differ in behavior or are coupled to connectable semantics:

- `MergeWith`
- `ZipWith`
- `Window`
- `GetAsyncEnumerator`
- `RunOn(TaskScheduler)` (semantic differences between implementations should be reviewed before unification)

**Decision gate before extraction**

- Define desired contract-level behavior in `README.md` (or dedicated operator spec) for each method:
  - ordering
  - completion behavior
  - error behavior
  - cancellation behavior
- For `Window`, explicitly choose chunked vs sliding semantics.
- For `MergeWith`, explicitly choose concurrent merge vs sequential concatenation semantics.
- For connectable-specific enumeration (`GetAsyncEnumerator`), keep implementation-specific logic unless a minimal primitive can express multicast semantics safely.

### Minimal Interface Candidate (Draft Direction)

A practical direction is to reduce `IStream<T>` to a tiny core and push the rest to extensions:

- `IAsyncEnumerable<T>` (already inherited)
- `IClock Clock { get; }`
- `string? Name { get; }`
- `IStream<T> Named(string name)` (optional to keep in contract; can also be extension if factory support is preferred)

Everything else becomes extension methods composed over `await foreach`, plus a small set of internal shared helpers for concurrency-heavy operators.

### Execution Order (Recommended)

1. Extract Phase 1 operators to extensions.
2. Introduce shared helper core for concurrency/retry/timeout.
3. Migrate Phase 2 operators to extensions backed by helper core.
4. Resolve Phase 3 behavior decisions and update docs/tests.
5. Remove redundant implementation-specific methods once coverage is green and behavior is confirmed.
