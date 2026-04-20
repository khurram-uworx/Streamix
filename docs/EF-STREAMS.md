# Entity Framework Streams Design

## Overview

This document outlines the design and implementation plan for Entity Framework Core integration with Streamix. The goal is to enable enterprise use cases where users can model business problems using reactive streams over database data.

## Packaging and dependencies

- **`Streamix` (core)** stays free of **Microsoft.EntityFrameworkCore**. It only knows about general streaming types (`IStream<T>`, etc.).
- **`Streamix.Extensions`** is the home for **optional integrations** that pull heavier dependencies (today: AsyncRx interop; this feature: EF Core).
- Adding EF support means **`Streamix.Extensions` gains a `PackageReference` to `Microsoft.EntityFrameworkCore`** (version aligned with the rest of the solution when implemented).
- **Consumers should expect a wider transitive dependency graph** when they reference `Streamix.Extensions`. That is an intentional tradeoff to avoid spinning up additional integration NuGet packages for each idea.

## Design Principles

1. **Minimal Requirements**: Place minimal constraints on entity models — only what EF Core itself requires.
2. **Provider Neutral**: Work with any EF Core database provider (SQL Server, PostgreSQL, SQLite, etc.).
3. **Reactive First**: Treat database queries as reactive data sources that can be composed with other streams.
4. **Resource Safety**: Make **DbContext lifetime explicit** — either the integration owns disposal (factory) or the caller does (caller-owned context). Never imply a context is safe to dispose while a query built on another context instance is still executing.
5. **Composition**: Enable EF streams to participate fully in the reactive stream ecosystem.

## Core Concepts

### Entity Framework Stream

An `IStream<T>` that:

- Executes an EF Core query when the stream is subscribed to (enumeration starts).
- Emits entities to downstream operators after the database work for that subscription has produced rows.
- Honors **cancellation** during asynchronous query execution.
- Propagates **errors** like any other Streamix pipeline.
- Can be composed with standard stream operators.

### Key Features

1. **Lazy execution**: The database work runs when the stream is consumed, not when the stream object is created.
2. **Cancellation**: Subscriber cancellation is passed through to EF’s async APIs where applicable.
3. **Resource management**: **Disposal is explicit by overload** (factory vs caller-owned context); see API Design.
4. **Composition**: Full participation in the stream operator ecosystem.
5. **Error handling**: Database and EF failures propagate; disposal still runs on failure/cancellation when the stream owns the context.

## API Design

Place public entry points in **`Streamix.Extensions`**. Core **`Stream`** factory methods live in the **`Streamix`** assembly; EF-backed factories use the same **static `From` / overload** style on **`EfStream`** in **`Streamix.Extensions`** (C# does not allow adding `partial class Stream` from another project). Optional extension-method sugar may wrap these factories; keep naming aligned with existing `Stream.From*` patterns.

`IOrderedQueryable<T>` is a shaped **`IQueryable<T>`**; **no separate API** is required for ordered queries.

### Critical lifetime rule

An **`IQueryable<T>` is bound to the `DbContext` it was built from**. It is incorrect to build a query from `dbContextA`, then execute it after creating `dbContextB` from a factory. The design **must** either:

- Build the query **inside** the same scope where the executing context is created (recommended factory shape below), or
- Execute a query that is still tied to a **caller-owned** context that remains alive for the whole subscription.

### Recommended: query builder + context factory

The stream creates a context per subscription, builds the query from that context, executes, then disposes the context.

```csharp
// Public factory type in Streamix.Extensions — mirrors Stream’s static factory style
public static class EfStream
{
    public static IStream<T> From<T>(
        Func<DbContext, IQueryable<T>> query,
        Func<DbContext> dbContextFactory,
        string? name = null)
        where T : class;

    // Optional: IClock for tests (if the implementation mirrors other Streamix sources)
    public static IStream<T> From<T>(
        Func<DbContext, IQueryable<T>> query,
        Func<DbContext> dbContextFactory,
        IClock clock,
        string? name = null)
        where T : class;
}
```

### Optional: caller-owned `DbContext`

For advanced scenarios, an overload may accept an **`IQueryable<T>`** (or a `DbContext` + delegate) with **documented** semantics:

- The stream **does not dispose** the `DbContext`.
- The caller **must** keep the context valid until that subscription completes (including on error paths).

This avoids a factory when the caller already manages unit-of-work scope.

### Usage Examples

```csharp
// Recommended: query is built from the same context instance the stream executes on
var customersStream = EfStream.From(
    ctx => ctx.Customers.Where(c => c.IsActive),
    () => new MyDbContext());

// Composition with other operators
var processedStream = customersStream
    .Map(customer => ProcessCustomer(customer))
    .Filter(customer => customer.OrderCount > 0)
    .Take(100);

// With cancellation
var cts = new CancellationTokenSource();
await processedStream.ForEachAsync(customer =>
{
    Console.WriteLine(customer.Name);
}, cts.Token);

// Error handling
var safeStream = customersStream
    .OnErrorResume(ex => Stream.Just(new Customer { Name = "Fallback" }))
    .Log("CustomerStream");
```

## Query execution and memory (v1)

EF Core does not expose a single provider-agnostic primitive that is both “true async” and “zero extra dependency” for every `IQueryable<T>`. For the **first implementation** in `Streamix.Extensions`:

- **Execute** with **`ToListAsync(CancellationToken)`** on the query built from the executing context, then **yield** each entity to the async enumerator.
- That means **the full result set for that subscription is materialized** before downstream operators see the first item. It is **not** row-by-row server streaming unless a later revision switches to an **`AsAsyncEnumerable()`**-style path (with clearly documented provider behavior and tradeoffs).

Document this honestly in release notes: **large queries pay full materialization cost** for v1.

## Implementation Components

### 1. EntityFrameworkStream&lt;T&gt; (or equivalent adapter)

- Internal type in **`src/Streamix.Extensions`** implementing `IStream<T>` **or** delegating to `Stream.From(...)` over an `IAsyncEnumerable<T>` that performs query execution.
- Owns **per-subscription** context creation/disposal when the factory overload is used.
- Applies **`ToListAsync`** (v1) then yields; respects cancellation between items when yielding from the in-memory list.

### 2. `EfStream` factory methods

- **`EfStream.From`** overloads colocated with other integration code in **`Streamix.Extensions`** (and optional extension-method wrappers).
- Validate arguments (null factories, null query delegate).
- Optional stream **name** for diagnostics (`Named` / `Log` / `Trace`).

### 3. Integration points

- Full participation in the stream operator ecosystem.
- **Do not** duplicate core operator implementations in the Extensions assembly unless unavoidable — prefer composing `Stream.From` and existing `IStream<T>` surfaces.

## Error Handling

### Expected errors (illustrative)

- **`OperationCanceledException`**: Subscriber cancelled or token linked cancellation fired during query execution.
- **`InvalidOperationException`**: Misconfigured query, disposed context, or EF usage errors.
- **Provider-specific exceptions** (e.g. **`SqlException`**): connectivity, timeouts, command failures.

**`DbUpdateException`** is typical of **`SaveChanges`** flows. It is less common for **read-only** queries but remains relevant when pipelines mix reads and writes.

### Error propagation

- Errors during query execution propagate through the stream.
- Standard Streamix operators (`OnErrorResume`, `Retry`, etc.) apply as usual.
- When the stream **owns** the context, **`DbContext` disposal** still occurs after failure or cancellation once execution scope ends.

## Performance Considerations

### Query execution

- **v1**: `ToListAsync` materializes the full result set per subscription; then items are pushed downstream one-by-one without retaining an extra buffer beyond that list.
- **Cold stream**: each subscriber runs the query again (unless composed with hot primitives like `Publish` upstream of sharing — still not “live DB subscription” by default).

### Concurrency

- With a **context factory**, each subscription should use **its own** `DbContext` instance (EF’s intended usage).
- **Concurrent operators** downstream (`Map(..., maxConcurrency)`, `FlatMap`, etc.) behave like any other Streamix pipeline; they do not change EF’s rule that **a single `DbContext` instance is not thread-safe**.

## Testing Strategy

### Test coverage areas

1. **Basic functionality**: Query execution and entity emission.
2. **Cancellation**: Cancellation during `ToListAsync` / enumeration.
3. **Error handling**: Provider or EF failures surface correctly.
4. **Resource safety**: Context disposed on success, failure, and cancellation when the factory overload owns the context.
5. **Composition**: `Map`, `Filter`, `Take`, etc.
6. **Lifetime correctness**: Query builder + factory executes on the **same** context instance; no “wrong context” footgun in examples.

### Test scenarios

- Successful query execution with representative entity types.
- Cancellation at different stages.
- Error handling and propagation.
- Multiple concurrent subscriptions (each with its own context when using factory).
- Integration with **`ConnectableStream`** if users share hot streams over EF sources.

Use **in-memory** or **SQLite** EF providers for automated tests where possible; avoid requiring external servers for CI.

## Enterprise Use Cases

### Real-world scenarios

1. **Dashboards**: Shape query results as streams for UI layers.
2. **Data processing pipelines**: Compose DB reads with Streamix operators.
3. **Microservices**: Read through streams at service boundaries (with clear materialization tradeoffs).
4. **Reporting**: Chunked or windowed processing over query results (mind memory for large sets in v1).

### Example: Order processing

```csharp
var newOrders = EfStream.From(
        ctx => ctx.Orders.Where(o =>
            o.Status == OrderStatus.New &&
            o.CreatedDate > DateTime.UtcNow.AddHours(-1)),
        () => new OrderDbContext())
    .Named("NewOrders");

var processedOrders = newOrders
    .FlatMap(order => ProcessOrderAsync(order), maxConcurrency: 10)
    .Log("OrderProcessor");

await processedOrders.ForEachAsync(async processedOrder =>
{
    await using var updateContext = new OrderDbContext();
    updateContext.Orders.Update(processedOrder);
    await updateContext.SaveChangesAsync();
});
```

## Roadmap

### Phase 1: Core implementation (current target)

- `Streamix.Extensions` package reference to **EF Core**.
- Factory-based **`EfStream.From`** (and optional caller-owned overload if needed).
- v1 materialization strategy (`ToListAsync` + yield).
- Tests in **`Streamix.Tests`** (project already references **`Streamix.Extensions`**).

### Phase 2: Advanced features

- Optional **streaming** execution path (`AsAsyncEnumerable`) with documented semantics.
- Transaction or unit-of-work patterns if required by consumers.
- Batch helpers where materialization strategy matters.

### Phase 3: Integration patterns

- CQRS / reporting patterns (documentation-first).
- Caching and invalidation guidance (out of scope for core code unless explicitly added).

## Non-Goals

1. **ORM replacement**: Not replacing EF Core’s ORM capabilities.
2. **New query DSL**: Not inventing a parallel query language.
3. **Database abstraction**: Not hiding EF Core behind a fake-neutral façade.
4. **Migrations tool**: Not managing schema migrations.

## Benefits

1. **Enterprise readiness**: Composes DB reads with Streamix pipelines.
2. **Reactive patterns**: Explicit async, cancellation, and error propagation.
3. **Composition**: Same operator ecosystem as the rest of Streamix.
4. **Testability**: EF in-memory / SQLite for automated tests.
5. **Provider flexibility**: Any EF Core provider, subject to that provider’s behavior.
