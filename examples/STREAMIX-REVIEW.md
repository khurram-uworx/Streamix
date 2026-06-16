# Streamix API Review

A developer experience review based on building the [`AIDataEngg`](AIDataEngg/) example — a
real-world pipeline that fetches RSS feeds, deduplicates into SQLite via EF Core, classifies
each item with a local SLM, and persists results.

## What Worked Well

### Fluent pipeline composition
`Stream.From(...).FlatMap(...).Checkpoint(...).FlatMap(...).DrainAsync(...)` reads
left-to-right like a LINQ query. No nesting, no ceremony. The mental model is clean:
build a stream → transform → terminate.

### `Retry` + `OnErrorReturn` composability
Chaining resilience operators inline:

```csharp
Stream.From(ct => ClassifyAsync(...))
    .Retry(3)
    .OnErrorReturn(fallback)
```

This reads naturally and integrates into any pipeline stage without wrapping in
try/catch blocks.

### `EfStream.FromStreamed`
Turns `IQueryable<T>` into a properly streamed source with automatic context lifecycle:

```csharp
EfStream.FromStreamed(
    ctx => ctx.Set<RssItem>().Where(r => !r.Processed),
    () => new RssDbContext())
```

Context creation/disposal is handled per subscription. No `ToListAsync` leak.

### `Checkpoint` for timing diagnostics
A single `.Checkpoint("Fetch")` in the pipeline prints per-item elapsed times. Useful
for spotting slow stages during development.

### `ScopedAsync` structured concurrency
```csharp
await Streamix.Stream.ScopedAsync(async scope =>
{
    var ct = scope.CancellationToken;
    // pipelines here
});
```

Clean scope management. Cancellation propagates naturally through all operators.

### `DrainAsync` terminal
When side effects happen inside `FlatMap` selectors (DB writes, console output),
`DrainAsync` is a clean "just consume everything" terminal that doesn't require
a dummy action.

## Friction Points

### `Stream` name collision with `System.IO.Stream`
The static factory class `Stream` collides with `System.IO.Stream` once `using Streamix;`
is in scope. Every call had to be fully qualified:

```csharp
Streamix.Stream.From(...)      // required
Stream.From(...)               // ambiguous — CS0104
```

This is the single most annoying paper cut. Every operator chain starts with it.
*Suggested rename:* **`Flux`** — short, unambiguous, globally searchable, no
namespace collisions, and already familiar to reactive programmers.
`Flux.From(urls).FlatMap(...)` reads clearly without qualification.
- Runners-up: `Streams` (minimal delta, but close to the original),
  `Flow` (intuitive but generic), `Sx` (branded but cryptic).
*Alternative:* keep `Stream` but make the extension methods work without the
factory class being in scope for common cases.

### No `IAsyncEnumerable` overload for `FlatMap`
`RssFetcher.FetchFeedAsync` returns `IAsyncEnumerable<RssItem>`. To flatten it:

```csharp
.FlatMap(url => Streamix.Stream.From(RssFetcher.FetchFeedAsync(url, ct)), maxConcurrency: 4)
```

Every fetch call requires wrapping in `Stream.From(...)`. An overload like:

```csharp
FlatMap<T, TResult>(this IStream<T> source,
    Func<T, IAsyncEnumerable<TResult>> selector,
    int maxConcurrency = int.MaxValue)
```

would remove the wrapping noise.

### No `DoOnNextAsync` for async side effects
`DoOnNext` (and its aliases `Tap`, `Do`) are synchronous only. When the side effect
is async (e.g., writing to a DB), you must embed it inside a `FlatMap` selector
or use `ForEachAsync`. This pushes side effects into places that look like
transformations.

A `DoOnNextAsync` that returns `IStream<T>` and takes `Func<T, Task>` or
`Func<T, ValueTask>` would keep the pipeline readable:

```csharp
.DoOnNextAsync(async item => await db.SaveChangesAsync(item))
```

### `FlatMap` overload density
Six overloads: `Task`, `IStream`, `ISingle`, `Ordered`, plus `Await` variants. The
compiler sometimes can't infer types when the lambda shape is ambiguous (CS0411).
*Suggestion:* ensure each overload is clearly differentiated by its parameter type
so inference almost always works. The order of overload resolution (Task vs IAsyncEnumerable
vs ISingle) would benefit from documentation.

### Single-item `From(Func<CancellationToken, Task<T>>)` + terminal
Wrapping an individual async call with retry/fallback requires:

```csharp
var result = await Stream
    .From(ct => ClassifyAsync(chatClient, item, goal, signalsText, ct))
    .Retry(3)
    .OnErrorReturn(fallback)
    .FirstAsync(ct);
```

The `From(...)` + `.FirstAsync()` dance feels like it should be a simpler primitive:

```csharp
var result = await Single
    .From(ct => ClassifyAsync(...))
    .Retry(3)
    .OnErrorReturn(fallback);
```

A `Single<T>` type (analogous to `IObservable<T>` → `Task<T>`) would make this
pattern first-class.

### `Stream.From(ct => ...)` factory vs `Stream.From(Task<T>)` distinction
The `Func<CancellationToken, Task<T>>` overload exists but isn't immediately obvious.
I initially reached for `Stream.From(task)` but needed cancellation support. The
difference between:

- `Stream.From(Task<T> task)` — eager, no CT
- `Stream.From(Func<CancellationToken, Task<T>> factory)` — lazy, CT-aware

is easy to miss without documentation.

### `CountAsync` consumes the stream
After calling `.CountAsync()`, the stream is consumed. In our pipeline we had to
re-create the `EfStream.FromStreamed` source to classify items after counting them.
A `CanCount` property on the source, or a peekable terminal, would avoid the
two-phase pattern.

### `Stream.From(Func<CancellationToken, Task<T>>)` returns `IStream<T>`, not `ISingle<T>`
The factory `Stream.From(Func<CancellationToken, Task<T>>)` at `Stream.cs:337` returns
`IStream<T>` (internally wrapping an `ISingle<T>`), not `ISingle<T>`. This means the
entire fluent chain — `From(...).Retry(3).OnErrorReturn(fallback).FirstAsync()` —
operates on `IStream<T>` throughout. The `ISingle<T>` interface methods (`Select`,
`Retry`, `OnErrorResume`, etc.) are never reached in practice; every call resolves
to the `IStream<T>` extension methods instead.

Consequence: when you need validation logic between `From(...)` and `Retry(...)`,
you cannot use `ISingle<T>.Select` to inject it — the compiler sees the `IStream<T>`
extensions. You must either: (a) push validation into the `From` factory lambda, or
(b) convert via `Stream.From(ISingle<T>)` to work with `IStream<T>.Select`.

### `OnErrorReturn` is fixed-value only; no access to exception
`OnErrorReturn(T value)` accepts a constant fallback — you cannot inspect the
exception. To extract information from the error (e.g., which invalid signal the
model returned), you must switch to `OnErrorResume(Func<Exception, IStream<T>>)`.
This is a natural limitation of the value-vs-function distinction, but it means
common fallback patterns (log-then-recover) require the more verbose API.

### `ISingle<T>` vs `IStream<T>` overload collision
The `ISingle<T>` interface declares `Select`, `Retry`, `OnErrorReturn`,
`OnErrorResume`, etc. as instance methods. The `Streamix.Extensions` namespace then
adds extension methods with the **same names** on `IStream<T>`. Since
`Stream.From(Func<CancellationToken, Task<T>>)` returns `IStream<T>`, the extension
methods win every time. This is technically correct (instance methods should be
preferred), but in practice the `ISingle<T>` interface methods are dead code — they
can never be called through the normal `From(...)` chain. This creates confusion
when reading the API docs: two types claim the same operators with different return
types, but only one path is actually reachable.

## Minor Observations

- **Cancel on disposal**: EF contexts from `EfStream.FromStreamed` are disposed when
  enumeration completes or is cancelled. This worked correctly in testing, including
  during concurrent enumeration.
- **`Checkpoint` output format**: prints to `Debug.WriteLine` by default, which
  doesn't appear in console output without a debugger attached.
- **Extension method discoverability**: operators are spread across
  `StreamExtensions.cs`, `LinqExtensions.cs`, `TerminalExtensions.cs`, and
  `EfStreamExtensions.cs`. For a library of this size, XML doc comments on every
  public method would significantly improve IDE intellisense experience.
- **No `OfType<T>()` or `Cast<T>()`**: For nullable narrowing after a filter
  (e.g., `FlatMap(..., () => null)` followed by `Where(x => x is not null)`),
  there's no type-safe way to go from `IStream<T?>` to `IStream<T>` without a
  manual `.Map(x => x!)`.

## Summary

| Area | Verdict |
|---|---|
| Composition model | Clean, LINQ-like, intuitive |
| Resilience (`Retry`, `OnErrorReturn`) | Excellent composability |
| EF integration (`EfStream`) | Well-designed, minimal ceremony |
| Diagnostics (`Checkpoint`) | Useful, could benefit from console-friendly output |
| `Stream` name collision | **Most impactful paper cut** — affects every pipeline |
| Async side effects | No `DoOnNextAsync`; forces embedding in `FlatMap` |
| `IAsyncEnumerable` FlatMap | Missing overload; requires wrapping |
| Documentation | No XML comments; hard to discover overloads |
| `Single<T>` pattern | Missing primitive for retry-wrapped single operations |

The library is in good shape for an early-stage project. The core composition model
is solid. The friction points are mainly around naming (the `Stream` collision),
missing overloads (`IAsyncEnumerable` FlatMap, `DoOnNextAsync`), and the lack of
XML documentation for IDE discoverability.
