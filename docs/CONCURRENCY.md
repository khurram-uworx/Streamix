# Streamix Concurrency Control: Explicit Semantics for Ordered/Unordered Operations

## Overview

Currently, Streamix provides concurrency control through scattered operators with inconsistent naming:
- `ParallelMap` / `ParallelMapOrdered` (1→1 transforms)
- `FlatMap` / `FlatMapMany` (1→N transforms)

The **ordering semantics are implicit and naming is inconsistent**. Users can't easily discover or understand whether results are:

- **Unordered** (fastest, results emit as soon as they complete)
- **Sequential** (ordered, but single-threaded with no concurrency)
- **Ordered concurrent** (concurrent execution with order-preserving output)

This ambiguity creates a **production concern**: developers must guess which semantic applies, potentially missing performance optimizations or ending up with unexpected result ordering.

## Proposal: Unified Concurrency API (Option C)

We will introduce a **unified, symmetric API** that makes the concurrency contract clear and discoverable:

```csharp
// Single-value transforms (1→1)
stream.Map(selector)                                    // unordered, unbounded
stream.MapOrdered(selector, maxConcurrency: 10)       // ordered, configurable concurrency

// Multi-value transforms (1→N flattening)
stream.FlatMap(selector)                               // unordered, unbounded
stream.FlatMapOrdered(selector, maxConcurrency: 10)   // ordered, configurable concurrency

// Sequential (single-threaded, always ordered)
stream.ConcatMap(selector)                            // sequential, strict order
```

### Semantic Comparison

| Operator | Concurrency | Ordering | Use Case | Performance |
|----------|-------------|----------|----------|-------------|
| `Map()` | Unbounded | Unordered | Fire-and-forget, fastest transformation | ⭐⭐⭐ |
| `MapOrdered()` | Configurable N | Ordered (reordered) | Transform with order preservation | ⭐⭐ |
| `FlatMap()` | Unbounded | Unordered | Fire-and-forget, fastest pipeline | ⭐⭐⭐ |
| `FlatMapOrdered()` | Configurable N | Ordered (reordered) | Flatten with order preservation | ⭐⭐ |
| `ConcatMap()` | 1 | Ordered (sequential) | Strict ordering, side effects that need order | ⭐ |

### Breaking Changes (0.x Release Cycle)

Since we're in a 0.x release cycle, we'll do a clean break:
- **Remove** `ParallelMap()` and `ParallelMapOrdered()` — replace with `Map()` and `MapOrdered()`
- **Remove** `FlatMapMany()` and `FlatMapManyAwait()` — replace with `FlatMap()`, `ConcatMap()`, and `FlatMapOrdered()`
- **No deprecation warnings** — clean API, no legacy baggage
- Update all internal usage to use new names

### Design Principles

1. **Symmetric naming** - `Map` ↔ `MapOrdered` and `FlatMap` ↔ `FlatMapOrdered` for easy discovery
2. **Unordered by default** - Methods without "Ordered" suffix are fastest (unbounded concurrency)
3. **Configurable concurrency** - Ordered variants accept `maxConcurrency` parameter for tuning
4. **Consistent with industry standards** - Aligns with RxJS, Rx.NET, and Project Reactor semantics

## Implementation Strategy

**Clean break, no deprecation** — We're in 0.x, so we'll remove old names entirely and implement fresh.

1. **Phase 1**: Remove old names (`ParallelMap`, `ParallelMapOrdered`, `FlatMapMany`, `FlatMapManyAwait`) from IStream
2. **Phase 2**: Add new signatures (`Map`, `MapOrdered`, `FlatMap`, `ConcatMap`, `FlatMapOrdered`)
3. **Phase 3**: Implement all five operators in Stream.cs and ConnectableStream.cs
4. **Phase 4**: Update all internal code and tests to use new names
5. **Phase 5**: Write comprehensive concurrency tests
6. **Phase 6**: Update README and documentation

## Files Affected

- `src/Streamix/IStream.cs` - Interface definitions
- `src/Streamix/Implementations/Stream.cs` - Implementation
- `src/Streamix/Implementations/ConnectableStream.cs` - Core logic for concurrent operations
- `src/Streamix.Tests/ConcurrencyTests.cs` - Concurrency validation tests
- `README.md` - Update examples and operator reference
- `docs/CONCURRENCY.md` - This file (design rationale)

---

# Task Breakdown

## ✅ Task 1: Remove Old Concurrency API and Define New Signatures

Status: Completed on 2026-04-05
One caveat: ISingle<T>.FlatMapMany still exists and remains in use where a Single expands to a stream; the
cleanup here is specifically the old IStream<T> concurrency API.

### Priority

High

### Goal

Clean break: Remove `ParallelMap`, `ParallelMapOrdered`, `FlatMapMany`, and `FlatMapManyAwait` from IStream; define new unified API signatures.

### Why this exists

We're in 0.x — no backward compatibility needed. Starting fresh with a clean, symmetric API is better than carrying legacy names.

### Scope

- **Remove** from `IStream.cs`: `ParallelMap()`, `ParallelMapOrdered()`, `FlatMapMany()`, `FlatMapManyAwait()`
- **Add** to `IStream.cs` (new signatures):
  - `Map<TResult>(Func<T, Task<TResult>> selector, int maxConcurrency = int.MaxValue)` — concurrent, unordered
  - `MapOrdered<TResult>(Func<T, Task<TResult>> selector, int maxConcurrency)` — concurrent, ordered
  - `FlatMap<TResult>(Func<T, IStream<TResult>> selector)` — concurrent, unordered
  - `ConcatMap<TResult>(Func<T, IStream<TResult>> selector)` — sequential, ordered
  - `FlatMapOrdered<TResult>(Func<T, IStream<TResult>> selector, int maxConcurrency)` — concurrent, ordered
- Write clear XML docs for each with concurrency/ordering guarantees
- Update method placement to group logically (Map family together, FlatMap family together)

### Constraints

- Clean slate — no wrappers or legacy support needed
- Must think through default parameters (e.g., should `Map` default to unbounded concurrency?)
- Interface is the contract — get it right before implementations start

### Suggested implementation path

1. Open `IStream.cs` and find the ParallelMap/FlatMapMany signatures
2. Delete the old signatures
3. Add the five new signatures with clear XML docs
4. Group methods by family (all Map variants together, all FlatMap variants together)
5. Review for consistency and completeness

### Acceptance criteria

- Old methods are removed from IStream.cs
- All five new methods are defined with clear signatures
- XML documentation explains concurrency, ordering, and performance characteristics
- No compilation errors
- Interface is symmetric and discoverable (users can easily find Map ↔ MapOrdered, FlatMap ↔ FlatMapOrdered)

### Documentation Updates

- [x] Update README.md operators section to remove `ParallelMap`, `ParallelMapOrdered`, and `IStream.FlatMapMany` references
- [x] Update README.md with new operator names: `Map`, `MapOrdered`, `FlatMap`, `ConcatMap`, `FlatMapOrdered`
- [ ] Add concurrency trade-off table to README (can reference docs/CONCURRENCY.md)

### Files likely involved

- `src/Streamix/IStream.cs`
- `README.md` (update operators list)

---

## Task 2: Implement Map and MapOrdered in Stream.cs and ConnectableStream.cs

### Priority

High

### Goal

Implement the single-value concurrent transforms (`Map` unordered, `MapOrdered` ordered).

### Why this exists

These are the building blocks. Once Map/MapOrdered work correctly with ordered reordering logic, FlatMap variants follow the same pattern.

### Scope

- Implement `Map<TResult>()` in `Stream.cs` — delegates to internal concurrent logic
- Implement `MapOrdered<TResult>()` in `Stream.cs` — delegates to internal ordered concurrent logic
- Implement corresponding async enumerables in `ConnectableStream.cs`
- Reuse/refactor existing `parallelMap()` and `parallelMapOrdered()` internal methods
- Ensure `Map()` has no reordering overhead (emit as results complete)
- Ensure `MapOrdered()` respects concurrency limit and reorders correctly

### Constraints

- Must not have concurrency or ordering bugs
- `Map` should have minimal overhead vs. current `ParallelMap`
- `MapOrdered` should match existing `ParallelMapOrdered` behavior

### Suggested implementation path

1. Look at current `parallelMap()` and `parallelMapOrdered()` implementations in ConnectableStream.cs
2. Keep them as-is (or refactor minimally for clarity)
3. Implement public `Map()` and `MapOrdered()` methods in Stream.cs that call internal methods
4. Test thoroughly before moving to FlatMap

### Acceptance criteria

- Both methods compile and are callable
- `Map()` emits results in completion order (no ordering)
- `MapOrdered()` emits results in source order despite concurrency
- Concurrency is limited by `maxConcurrency` parameter
- Existing `ParallelMap`/`ParallelMapOrdered` tests pass (update to use new names)

### Documentation Updates

- [ ] Update README.md basic pipeline example if it uses `ParallelMap` → use `Map` instead
- [ ] Update README.md concurrency section to show `Map`, `MapOrdered` examples (replace `ParallelMap`, `ParallelMapOrdered`)
- [ ] Update README.md operators list to reflect new names

### Files likely involved

- `src/Streamix/Implementations/Stream.cs`
- `src/Streamix/Implementations/ConnectableStream.cs`
- `README.md` (update examples and operators list)

---

## Task 3: Implement FlatMap, ConcatMap, FlatMapOrdered in Stream.cs and ConnectableStream.cs

### Priority

High

### Goal

Implement the multi-value concurrent transforms (FlatMap unordered, ConcatMap sequential, FlatMapOrdered ordered).

### Why this exists

These extend the Map family to handle 1→N transformations (flattening streams).

### Scope

- Implement `FlatMap<TResult>(Func<T, IStream<TResult>> selector)` — unordered, unbounded concurrency
- Implement `ConcatMap<TResult>(Func<T, IStream<TResult>> selector)` — sequential, ordered
- Implement `FlatMapOrdered<TResult>(Func<T, IStream<TResult>> selector, int maxConcurrency)` — ordered with configurable concurrency
- Implement corresponding async enumerables in `ConnectableStream.cs`
- Reuse/adapt existing `flatMapMany()` logic for FlatMap and ConcatMap
- Adapt `parallelMapOrdered()` pattern for FlatMapOrdered (handle streams, not single values)

### Constraints

- FlatMap must emit results as they complete (no buffering for order)
- ConcatMap must process strictly sequentially (one source item fully processed before next)
- FlatMapOrdered must buffer out-of-order results and emit in source order
- Concurrency limits must be respected

### Suggested implementation path

1. Start with `FlatMap` (simpler) — remove concurrency cap from existing `flatMapMany()`
2. Implement `ConcatMap` next (simplest) — just call existing sequential `flatMapMany()`
3. Implement `FlatMapOrdered` last (most complex) — adapt `parallelMapOrdered()` for streams

### Acceptance criteria

- All three methods compile and are callable
- `FlatMap()` emits results in completion order (fastest path)
- `ConcatMap()` processes sequentially, results in source order
- `FlatMapOrdered()` respects concurrency limit and reorders output correctly
- No values are dropped or duplicated
- Existing sequential tests pass (update to use new names)

### Documentation Updates

- [ ] Update README.md async composition example if it uses `FlatMapMany` → clarify between `FlatMap`, `ConcatMap`, `FlatMapOrdered`
- [ ] Update README.md concurrency section to show all five operators with clear use cases
- [ ] Update README.md operators list: remove `FlatMapMany`, `FlatMapManyAwait`; add `ConcatMap`, `FlatMapOrdered`
- [ ] Add concurrency semantics table to README (or link to docs/CONCURRENCY.md)

### Files likely involved

- `src/Streamix/Implementations/Stream.cs`
- `src/Streamix/Implementations/ConnectableStream.cs`
- `README.md` (update examples, operators list, concurrency section)

---

## Task 5: Update README and Public Documentation

### Priority

Medium

### Goal

Update README and example documentation to showcase the new unified API.

### Why this exists

The README is the first thing new users see. It should reflect the current recommended API.

### Scope

- Update README.md with new operator names (`Map`, `MapOrdered`, `FlatMap`, `ConcatMap`, `FlatMapOrdered`)
- Show symmetric API: `Map` ↔ `MapOrdered` and `FlatMap` ↔ `FlatMapOrdered`
- Include concurrency trade-off table (from CONCURRENCY.md)
- Update all examples using old names (`ParallelMap`, `ParallelMapOrdered`, `FlatMapMany`)
- Update any operator reference documentation
- Add link to CONCURRENCY.md for detailed semantics

### Suggested implementation path

1. Find concurrency/operators section in README.md
2. Replace `ParallelMap` examples with `Map`
3. Replace `ParallelMapOrdered` examples with `MapOrdered`
4. Clarify `FlatMap` vs. `ConcatMap` vs. `FlatMapOrdered` with use cases
5. Add comparison table from CONCURRENCY.md

### Acceptance criteria

- README reflects new API names
- Examples are clear and runnable
- Concurrency trade-offs are documented
- All references to old names are updated
- No broken links

### Files likely involved

- `README.md`
- `docs/CONCURRENCY.md`

---

## Coordination Notes

### Execution Order (Clean Break Approach)

1. **Task 1** → Remove old API, define new signatures (decision gate)
2. **Tasks 2–3** → Implement Map/MapOrdered and FlatMap/ConcatMap/FlatMapOrdered (can run in parallel)
3. **Task 4** → Comprehensive concurrency tests
4. **Task 5** → Comprehensive documentation review and README finalization

### Documentation Synchronization

**IMPORTANT**: README.md must stay in sync with code changes. Each implementation task (Tasks 1–3) includes specific `Documentation Updates` checklists.

- Tasks 1–3 include inline README updates (quick operator list, example fixes)
- Task 5 is the **final comprehensive documentation review** to ensure README, docs/CONCURRENCY.md, and XML docs are all consistent

**Before submitting Task 5**, verify:
- ✅ All old operator names removed from README
- ✅ All new operator names present with examples
- ✅ Concurrency trade-offs documented
- ✅ Examples are up-to-date and runnable
- ✅ No references to `ParallelMap`, `ParallelMapOrdered`, `FlatMapMany` remain (except in context of breaking changes)

### Shared Files

- `src/Streamix/IStream.cs` — modified in Task 1
- `src/Streamix/Implementations/Stream.cs` — modified in Tasks 2–3
- `src/Streamix/Implementations/ConnectableStream.cs` — modified in Tasks 2–3
- `src/Streamix.Tests/ConcurrencyTests.cs` — modified in Task 4
- `README.md` — modified in Tasks 1–3 (inline updates) and Task 5 (final review)

### Parallel Opportunities

- Tasks 2 and 3 can run in parallel (they modify different methods, but same files—coordinate on merges)
- Task 5 can start once Task 4 is complete (knows the new API and test coverage)

### Risk Mitigations

- **API shape risk**: Get Task 1 right—it's the foundation. Confirm signatures before implementations.
- **Correctness risk**: Comprehensive concurrency tests (Task 4) must verify edge cases (ordering, concurrency limits, stream merging)
- **Implementation risk**: Reuse existing `parallelMapOrdered()` and `flatMapMany()` logic; minimal new code = fewer bugs
- **Documentation drift risk**: Each task includes README updates; Task 5 is final audit to catch any missed updates
