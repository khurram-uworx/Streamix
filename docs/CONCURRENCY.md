# Streamix Concurrency Contract for 0.6

## Overview

This document records the settled 0.6 concurrency contract for Streamix. It is no longer a design proposal.

The key rule is that Streamix currently expresses `Map` concurrency by overload shape, not by operator name alone. That is an intentional 0.6 product decision and should not be reopened during follow-up docs work.

## Settled `Map` Contract

For 0.6, the shipped `Map` surface is:

```csharp
stream.Map(Func<T, TResult>)                              // sequential, ordered
stream.MapAwait(Func<T, ValueTask<TResult>>)             // sequential, ordered
stream.Map(Func<T, Task<TResult>>, maxConcurrency: N)    // concurrent, unordered
stream.MapOrdered(Func<T, Task<TResult>>, maxConcurrency: N) // concurrent, ordered
```

### Semantics Table

| Operator shape | Concurrency | Ordering | Notes |
|----------|-------------|----------|----------|
| `Map(Func<T, TResult>)` | 1 | Ordered | Synchronous projection over the source stream |
| `MapAwait(Func<T, ValueTask<TResult>>)` | 1 | Ordered | Async projection, but each item is awaited before the next item advances |
| `Map(Func<T, Task<TResult>>, int maxConcurrency = int.MaxValue)` | Configurable N, default unbounded | Unordered | Emits results as selector tasks complete |
| `MapOrdered(Func<T, Task<TResult>>, int maxConcurrency)` | Configurable N | Ordered | Runs selector tasks concurrently but buffers as needed to emit in source order |

## Confirmed Implementation Alignment

The current implementations in both `Stream<T>` and `ConnectableStream<T>` match the settled contract:

- `Map(Func<T, TResult>)` delegates to a sequential iterator-based mapping path
- `MapAwait(Func<T, ValueTask<TResult>>)` delegates to a sequential async iterator-based mapping path
- `Map(Func<T, Task<TResult>>, int maxConcurrency)` delegates to the concurrent unordered task-mapping path
- `MapOrdered(Func<T, Task<TResult>>, int maxConcurrency)` delegates to the concurrent ordered task-mapping path

Both implementations validate `maxConcurrency > 0` for the concurrent overloads.

## Related Operator Positioning

The broader 0.6 positioning around concurrency is:

| Operator | Concurrency | Ordering |
|----------|-------------|----------|
| `Map(Func<T, TResult>)` | 1 | Ordered |
| `MapAwait(Func<T, ValueTask<TResult>>)` | 1 | Ordered |
| `Map(Func<T, Task<TResult>>, ...)` | Configurable N | Unordered |
| `MapOrdered(...)` | Configurable N | Ordered |
| `FlatMap(...)` | Configurable N, default unbounded | Unordered |
| `ConcatMap(...)` | 1 | Ordered |
| `FlatMapOrdered(...)` | Configurable N | Ordered |

## Settled LINQ / Query-Syntax Scope

For 0.6, LINQ and query syntax are a convenience layer, not the full concurrency-control surface.

The settled contract is:

- `SelectMany` and `SelectManyAsync` remain unordered flattening helpers
- LINQ/query syntax does not expose ordered or sequential flattening controls for 0.6
- users who need explicit concurrency and ordering control should use fluent Streamix operators such as `FlatMap`, `ConcatMap`, and `FlatMapOrdered`

### Confirmed LINQ Surface Alignment

The current `LinqExtensions` implementation matches that contract:

- `Select(...)` delegates to `Map(Func<T, TResult>)`, so it is sequential and ordered
- `SelectAsync(...)` delegates to `MapAwait(Func<T, ValueTask<TResult>>)`, so it is sequential and ordered
- `SelectMany(...)` overloads delegate to `FlatMap(...)`, so they are unordered flattening helpers
- `SelectManyAsync(...)` composes through task-returning `Map(..., maxConcurrency)` plus `FlatMap(...)`, so it also remains unordered

This means query syntax should be understood as:

- ordered for `where` / `select`-style projection and filtering
- fastest-path unordered for flattening via `SelectMany`

## Guidance for Follow-on Docs Work

Task 4 should align LINQ and query-syntax docs to this settled scope without changing the extension surface. In particular, the following wording must be corrected where it appears:

- any wording that implies LINQ/query syntax exposes the same ordered/sequential flattening choices as the fluent API
- any examples that suggest `SelectMany` or `SelectManyAsync` preserve outer ordering
- any examples that present LINQ as the preferred path for ordered or sequential flattening

Task 2 should align public-facing docs to this settled contract without changing the API surface. In particular, the following wording must be corrected where it appears:

- any wording that describes `Map()` generically as unordered or fastest
- any examples that imply the synchronous `Map` overload is concurrent
- any summary tables that collapse all `Map` overloads into one concurrency story

README and XML docs should describe overload-specific behavior explicitly.
