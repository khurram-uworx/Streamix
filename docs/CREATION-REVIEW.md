# Streamix Creation Operators Review

## Scope

This review compares the plan in `docs/CREATION.md` against:

- the current public creation APIs in `src/Streamix/Stream.cs` and `src/Streamix/Single.cs`
- the creation-operator implementations in `src/Streamix/Implementations/Stream.cs` and `src/Streamix/Implementations/Single.cs`
- the current tests in `src/Streamix.Tests`
- the public contract described in `README.md`

## Executive Summary

The team has delivered the main Phase 1 creation story:

- `Single.From(Func<Task<T>>)` and `Single.From(Func<CancellationToken, Task<T>>)`
- `Single.Defer(...)`
- `Stream.Defer(...)`
- `Stream.Create(...)`
- `Stream.Generate(...)`
- `Stream.Interval(...)`
- README coverage for the new creation area

That said, there are still meaningful gaps. The biggest one is not a missing factory method. It is that `ISingle<T>` still does not clearly enforce or document its 0..1 contract when created from `IAsyncEnumerable<T>`. The plan explicitly called that out, and it remains unresolved.

There are also a few places where the implementation is good enough to pass current tests, but the semantics are looser than the plan said they should be.

## Delivery Status Against The Plan

### Implemented from the recommended first slice

- `Single.From(Func<Task<T>>)` is present.
- `Single.From(Func<CancellationToken, Task<T>>)` is present.
- `Single.Defer(Func<ISingle<T>>)` is present.
- `Single.Defer(Func<CancellationToken, ISingle<T>>)` is present.
- `Stream.Defer(Func<IStream<T>>)` is present.
- `Stream.Defer(Func<CancellationToken, IStream<T>>)` is present.
- `Stream.Create(...)` is present.
- `Stream.Generate(...)` is present in both sync and async forms.
- `Stream.Interval(TimeSpan)` and `Stream.Interval(TimeSpan, TimeSpan)` are present.

### Implemented beyond the suggested first slice

- `Stream.Generate(...)` already shipped, even though the plan suggested it could land after the first slice.
- `Stream.From(Func<Task<T>>)` and `Stream.From(Func<CancellationToken, Task<T>>)` also exist via `Single.From(...)`, which improves boundary coverage further than the plan required.

### Not implemented from later phases

These are still missing, but they are phase 2/3 items rather than regressions:

- `Stream.From(IEnumerable<T>)`
- `Stream.From(params T[] items)`
- `Stream.From(Func<CancellationToken, IAsyncEnumerable<T>>)`
- `Single.From(ValueTask<T>)`
- `Single.From(Func<ValueTask<T>>)` and token overload
- `Using(...)`
- `Poll(...)`
- `Stream.Never<T>()`
- `Stream.Timer(TimeSpan)`
- event/callback helpers built on top of `Create(...)`

## Findings

### 1. `ISingle<T>` cardinality is still underspecified and not enforced

This is the main thing the team should not consider done.

The plan said:

- `Single` creation helpers should preserve the 0..1 contract
- if `Single.From(IAsyncEnumerable<T>)` remains permissive, document current behavior clearly

Current state:

- `Single.From(IAsyncEnumerable<T>)` wraps any async enumerable directly.
- `ISingle<T>` still inherits `IAsyncEnumerable<T>`, so callers can enumerate multiple items from a supposed single.
- `Single<T>.ToTask()` returns the first observed item and silently ignores later ones.
- `Single<T>.Retry(...)` explicitly stops after the first item, but plain enumeration and `ForEachAsync(...)` do not.

Impact:

- The public type says "0 or 1 item", but the implementation does not consistently enforce that.
- Different terminal paths observe different behavior on the same invalid source.
- This is a contract hole, not just a missing test.

Recommended action:

- Either enforce cardinality in `Single.From(IAsyncEnumerable<T>)` and in `Single<T>` enumeration paths, or
- explicitly document that this overload trusts the caller and may surface undefined behavior if the source emits more than one item

Priority: High

### 2. `Stream.Create(...)` shipped with a narrower signature than the plan proposed

The plan recommended:

```csharp
Func<IStreamEmitter<T>, CancellationToken, ValueTask>
```

Current API:

```csharp
Func<IStreamEmitter<T>, Task>
```

This is workable because the emitter exposes `CancellationToken`, but it still leaves two differences from plan intent:

- the producer does not receive the subscription token as an explicit parameter
- `ValueTask` is not supported

Impact:

- not a correctness bug
- but it is an API-shape deviation from the intended design and slightly less idiomatic for low-allocation async producers

Recommended action:

- Decide whether the current shape is the intended final public contract.
- If yes, update `docs/CREATION.md` to match reality.
- If not, add the planned overload rather than replacing the current one.

Priority: Medium

### 3. `Stream.Create(...)` terminal behavior is only partially locked down by tests

The plan asked for:

- completion and failure idempotence
- predictable behavior for emissions after terminal state
- explicit tests for "producer throws after terminal signal"

Current state:

- idempotent terminal methods are implemented
- tests cover idempotent terminal state
- tests cover producer exception before terminal state
- there is no test for producer exception after `Complete()` or `Fail(...)`
- `Emitter.EmitAsync(...)` throws if terminal state is already visible before write, but swallows `ChannelClosedException` and `OperationCanceledException` during write races

Impact:

- the implementation likely behaves acceptably in practice
- but the exact contract for late producer mistakes is still fuzzy

Recommended action:

- Add tests for:
  - producer throws after `Complete()`
  - producer throws after `Fail(...)`
  - `EmitAsync(...)` after terminal state
- Then decide whether late emits should throw consistently or be silently ignored

Priority: Medium

### 4. The current backpressure test for `Create` is too weak

`Create_Supports_Backpressure` proves that items can be consumed sequentially. It does not prove that the producer actually blocks when the consumer is slow.

Why this matters:

- the implementation currently uses `Channel.CreateBounded<T>(1)`, so the intended behavior is probably correct
- but the test does not lock that in

Recommended action:

- replace or supplement the test with one that verifies the second `EmitAsync(...)` does not complete until the first item is consumed
- use `TaskCompletionSource` or `SemaphoreSlim` so the assertion is about producer blocking, not just output order

Priority: Medium

### 5. `Create` cancellation semantics should be documented more explicitly

Current implementation cancels the emitter token when:

- the consumer cancels
- the consumer disposes
- `Complete()` is called
- `Fail(...)` is called

That means `emitter.CancellationToken` represents "the producer should stop now", not only "the consumer canceled".

Impact:

- the behavior is reasonable
- but producers may interpret that token as external cancellation only

Recommended action:

- document this in README or XML docs for `IStreamEmitter<T>`
- especially if the team wants event/callback adapters to be built on `Create(...)`

Priority: Medium

### 6. Lazy vs eager semantics are still not fully documented

The plan correctly distinguished:

- eager `Single.From(Task<T>)`
- lazy `Single.From(Func<Task<T>>)`
- lazy `Stream.Defer(...)`

Current code implements that distinction, but README does not make the eager vs lazy boundary explicit.

Impact:

- callers can accidentally start work too early by passing a hot `Task<T>`
- this was one of the original motivations for the new factories

Recommended action:

- add one short note to the creation section clarifying:
  - `From(Task<T>)` wraps existing work
  - `From(Func<Task<T>>)` defers work until subscription

Priority: Medium

### 7. Some planned semantic tests are still missing

The current creation test suite is solid, but not complete relative to the plan.

Missing or under-covered cases:

- `Single.Defer(Func<CancellationToken, ISingle<T>>)` invoked once per subscription
- `Single.Defer(Func<CancellationToken, ISingle<T>>)` token propagation, not just cancellation outcome
- `Stream.Interval(...)` invalid-argument coverage for negative `dueTime` and non-positive `period`
- `Generate(...)` repeated subscription behavior
- `Create(...)` producer throws after terminal signal

Priority: Low to Medium

## README Alignment

README is directionally aligned with the shipped feature set. The dedicated creation section exists and covers:

- `Stream.Create<T>`
- `Stream.Defer<T>` / `Single.Defer<T>`
- `Stream.Generate<TState, T>`
- `Stream.Interval`
- `Stream.From` / `Single.From` / `Just`

The main README follow-up still worth doing is semantic clarification, not feature listing:

- clarify eager vs lazy behavior for task-based overloads
- clarify `Create` cancellation behavior
- clarify the actual `Single.From(IAsyncEnumerable<T>)` cardinality contract

## Validation

Focused validation was run against the creation-operator-related tests:

```text
dotnet test src\Streamix.Tests\Streamix.Tests.csproj --configuration Release --filter "CreateTests|DeferTests|GenerateTests|SingleFactoryTests|TimeBasedOperatorTests"
```

Result:

- Passed: 37
- Failed: 0
- Skipped: 0

There were unrelated compiler warnings in other test files, but the targeted creation review checks passed.

## Recommended Next Steps

1. Resolve the `ISingle<T>` cardinality contract explicitly. This is the highest-value follow-up.
2. Decide whether the shipped `Stream.Create(...)` signature is final or whether a `CancellationToken` plus `ValueTask` overload should still be added.
3. Strengthen the `Create` semantics tests, especially backpressure and post-terminal producer behavior.
4. Tighten README wording around lazy vs eager factories and `Create` cancellation semantics.
5. Keep phase 2 and phase 3 items in backlog rather than treating them as misses for this delivery.

## Bottom Line

The team did not miss the core creation-operator delivery. The main slice is implemented and tested.

What is still missing is contract hardening:

- `ISingle<T>` still has an unresolved 0..1 semantics gap
- `Create(...)` needs a clearer final API decision
- a few important semantics are implemented but not yet fully locked down by tests or documentation
