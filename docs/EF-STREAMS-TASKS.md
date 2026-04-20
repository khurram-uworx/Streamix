# Entity Framework Streams Task Breakdown

## Purpose

This document breaks the Entity Framework Core integration feature into concrete, assignable tasks for coding agents.

Use `docs/TASKS-TEMPLATE.md` when adding new workstreams; this file follows the same pattern (decision gates, acceptance criteria, likely files).

## Suggested Execution Order

1. Task 1: Design review and API contract finalization (decision gate)
2. Task 2: Implement EF stream adapter and execution path in `Streamix.Extensions`
3. Task 3: Extension methods and XML documentation
4. Task 4: Integration testing and validation (`Streamix.Tests`)
5. Task 5: Documentation (README / package README / design note alignment)
6. Task 6: Performance and materialization review (post–Phase 1)

## Coordination Notes

- **Task 1 is a decision gate.** Do not land public API in code until `docs/EF-STREAMS.md` and signatures match.
- **Task 2 and Task 3** touch the same project (`Streamix.Extensions`); one owner or strict sequencing to avoid merge pain.
- **`Streamix.Extensions` transitive dependencies grow** when EF Core is added; note that in package README so AsyncRx-only consumers understand the tradeoff (or document mitigations if the team later splits packages).
- **Lifetime rule is non-negotiable:** query must be built from the **same** `DbContext` instance that executes it (factory + `Func<DbContext, IQueryable<T>>`), unless the caller-owned overload is implemented and documented.
- Shared files with merge-conflict risk:
  - `src/Streamix.Extensions/Streamix.Extensions.csproj`
  - `src/Streamix.Extensions/**/*.cs` (new or restored from excluded sources)
  - `src/Streamix.Tests/Streamix.Tests.csproj`
  - `src/Streamix.Tests/EfStreamTests.cs` (new)
  - `README.md`, `src/Streamix.Extensions/README.md`, `docs/EF-STREAMS.md`

## ✅ Task 1: Finalize EF Stream Design and API Contract

### Priority

High

### Goal

Freeze the public surface and semantics for EF-backed streams so implementation does not re-litigate context lifetime or materialization behavior.

### Why this exists

EF integration is easy to get subtly wrong (wrong `DbContext`, hidden materialization cost). The design note must match what ships.

### Decision required

- Final **`EfStream`** method overloads (`From`, optional caller-owned variant), **namespace**, and whether any **extension-method** sugar wraps the static factory.
- Confirm **primary** entry point: `Func<DbContext, IQueryable<T>>` + `Func<DbContext>` factory.
- Confirm whether **caller-owned context** overload ships in Phase 1 or later.
- Confirm **v1** uses `ToListAsync` + yield (full materialization per subscription).
- Confirm **IClock** / naming parity with other Streamix sources.

### Scope

- Keep `docs/EF-STREAMS.md` the source of truth; update if API names change.
- Ensure examples are **lifetime-correct** and **compile-realistic** (async terminals, valid EF patterns).
- List breaking changes if a stub existed with a different shape.

### Constraints

- **No `Microsoft.EntityFrameworkCore` reference in `Streamix` (core).**
- Integration code lives in **`Streamix.Extensions`**.
- Must work with any EF Core provider supported by the chosen EF version.

### Suggested implementation path

- Compare with existing `From*` / resource-owning patterns in core (`Stream.Using`, etc.).
- Resolve any excluded prototype sources under `Streamix.Extensions` (see csproj `Compile Remove`).

### Acceptance criteria

- `docs/EF-STREAMS.md` reflects agreed API and packaging.
- Examples demonstrate **correct** context lifetime (no query built on one context and executed on another).
- Materialization cost and cancellation behavior are explicitly stated.

### Files likely involved

- `docs/EF-STREAMS.md`
- `docs/EF-STREAMS-TASKS.md`
- `src/Streamix.Extensions/Streamix.Extensions.csproj`

## ✅ Task 2: Implement EF Stream Adapter in Streamix.Extensions

### Priority

High

### Goal

Provide an internal (or minimal) adapter that turns an EF query into `IStream<T>` behavior with correct disposal and cancellation for the factory-based entry point.

### Scope

- Add **`PackageReference`** to **`Microsoft.EntityFrameworkCore`** on **`Streamix.Extensions`** (version per repo conventions).
- Remove or replace **`Compile Remove`** entries that hide prototype files if the team adopts them; otherwise add new implementation files.
- Implement **per-subscription** `DbContext` create / dispose for the factory overload.
- **v1 execution:** `ToListAsync(cancellationToken)` then yield items (see design doc).
- Prefer **delegating** to `Stream.From(...)` / `IAsyncEnumerable<T>` rather than hand-rolling every `IStream<T>` combinator unless required.

### Constraints

- Do not block threads for query execution; use EF async APIs.
- Nullable reference types and `TreatWarningsAsErrors` must stay clean.
- `DbContext` is **not thread-safe**; document or enforce patterns for concurrent downstream operators (same as any EF usage).

### Suggested implementation path

- Start with `GetAsyncEnumerator`: open context, build query from delegate, `ToListAsync`, yield.
- Add tests early for **context disposal** and **cancellation**.

### Acceptance criteria

- `Streamix.Extensions` builds with EF Core referenced.
- Factory-based path disposes context on success, failure, and cancellation.
- Query uses the **same** context instance as the query builder delegate.

### Files likely involved

- `src/Streamix.Extensions/Streamix.Extensions.csproj`
- `src/Streamix.Extensions/EfStream.cs` (public static factory type) and/or internal adapter (for example `EntityFrameworkStream<T>`)
- `src/Streamix.Extensions/*.cs` (helpers as needed)

## ✅ Task 3: Add Extension Methods and Fluent API

### Priority

High

### Goal

Expose discoverable, documented **`EfStream.From`** overloads and optional extension-method sugar; signatures must match Task 1 and `docs/EF-STREAMS.md`.

### Scope

- Public extension methods in **`Streamix.Extensions`** only.
- Parameter validation (null checks, meaningful exceptions).
- XML documentation describing **context lifetime** and **materialization**.

### Constraints

- Do not imply row-by-row DB streaming in docs if v1 materializes via `ToListAsync`.

### Suggested implementation path

- Mirror patterns from core `Stream` factory methods and `Streamix.Extensions` AsyncRx entry points.

### Acceptance criteria

- IntelliSense-friendly API with clear overload differences.
- XML docs warn against wrong-context query composition.

### Files likely involved

- `src/Streamix.Extensions/*.cs` (new extension class if split from adapter)
- `src/Streamix.Extensions/README.md` (consumer-facing package note)

## ✅ Task 4: Integration Testing and Validation

### Priority

High

### Goal

Prove correctness, cancellation, disposal, and composition using automated tests.

### Scope

- Add **`Microsoft.EntityFrameworkCore.InMemory`** (or SQLite) to **`Streamix.Tests`** if not already present for this feature.
- Tests: success path, cancellation during query, exception propagation, **multiple subscriptions** with factory (distinct contexts).
- Composition tests: `Map`, `Filter`, `Take`, terminal `ForEachAsync`.

### Constraints

- No external database server required for CI.
- Avoid timing-only assertions where deterministic synchronization is possible.

### Suggested implementation path

- Small `DbContext` + entity types scoped to tests.
- Use `CancellationTokenSource` to cancel mid-enumeration.

### Acceptance criteria

- New tests fail on pre-implementation / wrong-lifetime behavior and pass on correct implementation.
- `dotnet test --configuration Release` succeeds.

### Files likely involved

- `src/Streamix.Tests/Streamix.Tests.csproj`
- `src/Streamix.Tests/EfStreamTests.cs` (new)

## Task 5: Documentation and Examples

### Priority

Medium

### Goal

Ship accurate public documentation so users understand dependency cost and EF lifetime rules.

### Scope

- Short section in root **`README.md`** pointing to **`Streamix.Extensions`** for EF (once API exists); avoid claiming the feature before it builds.
- Update **`src/Streamix.Extensions/README.md`** with EF usage, **NuGet transitive dependency** note, and a minimal example.
- Keep **`docs/EF-STREAMS.md`** aligned with shipped API.

### Constraints

- Examples must compile against the actual public API.
- Do not duplicate long prose in three places; link to `docs/EF-STREAMS.md` for depth.

### Acceptance criteria

- README / package README / design doc do not contradict each other.
- Call out v1 materialization (`ToListAsync`) honestly.

### Files likely involved

- `README.md`
- `src/Streamix.Extensions/README.md`
- `docs/EF-STREAMS.md`

## Task 6: Performance Optimization and Materialization Review

### Priority

Medium

### Goal

After Phase 1 works, decide whether to add a streaming execution path or other optimizations without breaking semantics.

### Scope

- Evaluate `AsAsyncEnumerable` (or provider-specific patterns) for large-result scenarios.
- Measure memory for large lists; document tuning (batching, limits, projection).

### Constraints

- Any new execution mode must be documented with ordering, cancellation, and provider caveats.

### Acceptance criteria

- Written recommendation in `docs/EF-STREAMS.md` (Phase 2 section updated) or a short addendum.
- If code changes land, they include targeted tests.

### Files likely involved

- `docs/EF-STREAMS.md`
- `src/Streamix.Extensions/**/*.cs`

## Suggested Agent Handout Batches

### Batch A: decision-critical

- Task 1

### Batch B: implementation

- Task 2
- Task 3

### Batch C: tests and docs

- Task 4
- Task 5

### Batch D: follow-up

- Task 6

## Final Checklist

- Every task has a clear owner-sized scope
- Every task has acceptance criteria
- Decision-gate tasks are clearly marked
- Likely files are listed to reduce agent search time
- Execution order reflects real dependencies
- Packaging impact (`Streamix.Extensions` + EF Core) is explicit
