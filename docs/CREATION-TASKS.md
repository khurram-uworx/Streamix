# Streamix Creation Follow-Up Tasks

## Purpose

This document breaks the findings from `docs/CREATION-REVIEW.md` into concrete, assignable tasks for coding agents.

These tasks are focused on creation-operator contract hardening, test coverage, and documentation alignment. They are intentionally scoped so they can be handed out independently where practical.

## Suggested Execution Order

1. Task 1: resolve `ISingle<T>` cardinality direction
2. Task 2A or 2B: finalize `Stream.Create(...)` API direction
3. Task 3: strengthen `Create(...)` semantics tests
4. Task 4: add missing creation-operator semantic tests
5. Task 5: update README and XML docs for finalized semantics
6. Task 6+: pick up phase 2 convenience factories as separate feature work

## Coordination Notes

- Do not run documentation-only tasks in parallel with API-shape tasks until the API decision is made.
- Test-only tasks can run in parallel with documentation updates if they are not changing public surface area.
- Task 1 is the highest-priority decision gate because it affects API contract, docs, and future operator behavior.
- Task 2 is also a decision gate because the README and `docs/CREATION.md` should reflect the final `Create(...)` signature.

## Task 1: Resolve `ISingle<T>` Cardinality Contract

### Priority

High

### Goal

Make `ISingle<T>` behave consistently with its advertised 0..1 contract, or explicitly document that `Single.From(IAsyncEnumerable<T>)` is permissive and caller-trusting.

### Why this exists

`ISingle<T>` currently claims 0..1 semantics, but `Single.From(IAsyncEnumerable<T>)` accepts arbitrary sequences and different terminal paths behave differently when more than one item is present.

### Decision required

Choose one of these directions:

- enforce cardinality at runtime
- preserve permissive behavior and document it explicitly

### Scope

- inspect `src/Streamix/Single.cs`
- inspect `src/Streamix/Implementations/Single.cs`
- inspect `src/Streamix/ISingle.cs`
- update behavior and docs consistently based on the chosen direction
- add or update tests in `src/Streamix.Tests`

### Suggested implementation paths

If enforcing cardinality:

- detect second-item emission and fail predictably
- make `ToTask()`, `ForEachAsync(...)`, plain enumeration, and retry-related paths consistent
- choose the exception type and document it

If staying permissive:

- document that `Single.From(IAsyncEnumerable<T>)` trusts the caller
- document what `ToTask()` does when multiple items are produced
- add tests that lock in the chosen behavior

### Acceptance criteria

- one explicit project decision is implemented
- behavior is consistent across enumeration and terminal methods
- README and XML docs reflect the decision
- tests cover the chosen semantics

### Files likely involved

- `src/Streamix/Single.cs`
- `src/Streamix/Implementations/Single.cs`
- `src/Streamix/ISingle.cs`
- `src/Streamix.Tests/SingleFactoryTests.cs`
- `README.md`

## Task 2A: Keep Current `Stream.Create(...)` Signature And Align Docs

### Priority

Medium

### Goal

If the team decides the existing `Func<IStreamEmitter<T>, Task>` signature is the intended public API, align the plan and docs to that decision.

### Scope

- update `docs/CREATION.md`
- update README creation examples if wording implies the planned signature
- tighten XML docs around the emitter contract

### Acceptance criteria

- `docs/CREATION.md` no longer describes a different target API than the code ships
- `README.md` describes the current `Create(...)` shape accurately
- emitter token semantics are documented clearly

### Files likely involved

- `docs/CREATION.md`
- `README.md`
- `src/Streamix/Interfaces.cs`
- `src/Streamix/Stream.cs`

## Task 2B: Add Planned `Stream.Create(...)` Overload

### Priority

Medium

### Goal

If the team still wants the planned shape, add an overload that accepts explicit cancellation and supports `ValueTask`.

### Target API

```csharp
public static IStream<T> Create<T>(
    Func<IStreamEmitter<T>, CancellationToken, ValueTask> producer)
```

### Scope

- add the overload without breaking the current API
- route both overloads through a shared implementation
- preserve current backpressure, cancellation, and terminal behavior
- add tests for both overloads
- update docs to show the preferred overload

### Acceptance criteria

- both overloads compile and behave consistently
- cancellation token passed to the new overload matches subscription lifecycle
- focused creation tests pass

### Files likely involved

- `src/Streamix/Stream.cs`
- `src/Streamix/Implementations/Stream.cs`
- `src/Streamix.Tests/CreateTests.cs`
- `docs/CREATION.md`
- `README.md`

## Task 3: Harden `Stream.Create(...)` Semantics Tests

### Priority

Medium

### Goal

Lock down the important `Create(...)` semantics that are currently only partially tested.

### Scope

- strengthen backpressure verification
- add post-terminal behavior tests
- add late-emission behavior tests

### Required tests

- producer blocks on second `EmitAsync(...)` until the consumer advances
- producer throws after `Complete()` and the stream stays in a valid terminal state
- producer throws after `Fail(...)` and the original terminal state wins consistently
- `EmitAsync(...)` after terminal state behaves according to the documented contract

### Notes

- this task should not change public API by itself
- if test failures reveal ambiguous implementation behavior, document that in the PR notes

### Acceptance criteria

- new tests fail before the fix or prove current behavior intentionally
- tests are deterministic and do not rely on arbitrary sleeps where avoidable
- focused creation test suite passes

### Files likely involved

- `src/Streamix.Tests/CreateTests.cs`
- possibly `src/Streamix/Implementations/Stream.cs` if semantics need tightening

## Task 4: Fill Remaining Creation Test Gaps

### Priority

Low to Medium

### Goal

Close the missing semantic coverage identified in the review for defer, generate, and interval.

### Scope

- `Single.Defer(Func<CancellationToken, ISingle<T>>)` invoked once per subscription
- token propagation for `Single.Defer(Func<CancellationToken, ISingle<T>>)`
- repeated-subscription behavior for `Generate(...)`
- invalid-argument coverage for `Stream.Interval(...)`

### Acceptance criteria

- each missing case has a focused, readable test
- new tests reflect actual intended semantics rather than implementation accidents
- no unrelated production refactors

### Files likely involved

- `src/Streamix.Tests/SingleFactoryTests.cs`
- `src/Streamix.Tests/GenerateTests.cs`
- `src/Streamix.Tests/TimeBasedOperatorTests.cs`

## Task 5: README And XML Doc Clarification Pass

### Priority

Medium

### Goal

Make the creation story truthful and precise now that the first slice is implemented.

### Scope

- clarify eager vs lazy task-based creation
- clarify `Create` cancellation semantics
- clarify finalized `ISingle<T>` cardinality behavior
- ensure examples only show APIs and semantics that actually exist

### Constraints

- do not document future phase 2 or phase 3 factories as already shipped
- do not update docs until Task 1 and Task 2 direction is settled

### Acceptance criteria

- README creation section is aligned with actual code
- XML docs for creation APIs match runtime behavior
- examples remain executable in spirit

### Files likely involved

- `README.md`
- `src/Streamix/Interfaces.cs`
- `src/Streamix/Stream.cs`
- `src/Streamix/Single.cs`

## Task 6: Add `Stream.From(IEnumerable<T>)` And `Stream.From(params T[] items)`

### Priority

Medium

### Goal

Add the highest-value phase 2 convenience factories for tests, examples, and basic boundaries.

### Scope

- add `Stream.From(IEnumerable<T>)`
- add `Stream.From(params T[] items)`
- ensure cold behavior across subscriptions
- add tests for empty, single-item, and multi-item cases
- update README examples where these overloads improve clarity

### Acceptance criteria

- overload resolution is sensible and unambiguous
- streams are cold across repeated subscriptions
- tests cover ordering and cancellation behavior where relevant

### Files likely involved

- `src/Streamix/Stream.cs`
- `src/Streamix.Tests/StreamTests.cs`
- `README.md`

## Task 7: Add Lazy Async-Enumerable And ValueTask Factories

### Priority

Medium

### Goal

Expand boundary coverage with the remaining phase 2 async-first factories.

### Target APIs

```csharp
public static IStream<T> From<T>(Func<CancellationToken, IAsyncEnumerable<T>> factory)
public static ISingle<T> From<T>(ValueTask<T> valueTask)
public static ISingle<T> From<T>(Func<ValueTask<T>> factory)
public static ISingle<T> From<T>(Func<CancellationToken, ValueTask<T>> factory)
```

### Scope

- add the new overloads
- preserve lazy semantics for factory-based forms
- ensure cancellation is forwarded correctly
- add focused tests

### Acceptance criteria

- overloads behave lazily where expected
- `ValueTask` paths do not regress task-based behavior
- tests cover success, cancellation, and exception propagation

### Files likely involved

- `src/Streamix/Stream.cs`
- `src/Streamix/Single.cs`
- `src/Streamix.Tests/SingleFactoryTests.cs`
- `src/Streamix.Tests/StreamTests.cs`

## Task 8: Add `Stream.Never<T>()` And `Stream.Timer(TimeSpan)`

### Priority

Low to Medium

### Goal

Add the next two lightweight reactive primitives identified in the plan.

### Scope

- add `Stream.Never<T>()`
- add `Stream.Timer(TimeSpan)`
- add tests for completion, cancellation, and timing semantics
- document both operators in README

### Acceptance criteria

- `Never<T>()` emits nothing and never completes unless cancelled
- `Timer(TimeSpan)` emits `0L` once after due time and completes
- timing tests use the clock abstraction rather than real time

### Files likely involved

- `src/Streamix/Stream.cs`
- `src/Streamix.Tests/TimeBasedOperatorTests.cs`
- `README.md`

## Task 9: Evaluate `Using(...)` As A Phase 3 Resource-Scoped Factory

### Priority

Low

### Goal

Design and, if approved, implement a resource-scoped creation operator that fits Streamix idioms.

### Scope

- validate whether `IAsyncDisposable` only is sufficient
- decide whether `IDisposable` overloads are needed
- define cancellation and disposal ordering
- add tests for success, error, and cancellation cleanup

### Acceptance criteria

- design is documented before code lands
- resource disposal order is explicit and tested
- no hidden lifetime leaks

### Files likely involved

- `docs/CREATION.md`
- `src/Streamix/Stream.cs`
- `src/Streamix.Tests/ResourceSafetyTests.cs`
- `README.md`

## Task 10: Evaluate `Poll(...)` Or Document The Composition Pattern

### Priority

Low

### Goal

Decide whether polling deserves a first-class factory or should remain a documented composition built from existing primitives.

### Scope

- prototype `Interval + FlatMap/FlatMapAwait` composition
- compare ergonomics with a dedicated `Poll(...)` API
- if dedicated API is justified, implement and test it
- otherwise add a README example showing the recommended composition

### Acceptance criteria

- one explicit direction is chosen
- if implemented, cancellation and overlap semantics are documented
- if not implemented, README provides a truthful composition example

### Files likely involved

- `README.md`
- potentially `src/Streamix/Stream.cs`
- potentially `src/Streamix.Tests/TimeBasedOperatorTests.cs`

## Suggested Agent Handout Batches

### Batch A: decision-critical

- Task 1
- Task 2A or 2B

### Batch B: test hardening

- Task 3
- Task 4

### Batch C: documentation alignment

- Task 5

### Batch D: next feature slice

- Task 6
- Task 7
- Task 8

### Batch E: design-heavy later work

- Task 9
- Task 10
