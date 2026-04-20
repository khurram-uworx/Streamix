# Streamix.Extensions

AsyncRx.NET interop for Streamix.

This package provides the bridge between Streamix and [AsyncRx.NET](https://github.com/dotnet/reactive) without forcing the core `Streamix` package to depend on preview AsyncRx bits.

It also hosts optional Entity Framework Core integration via `EfStream`.

## Maturity and Dependency Isolation

As AsyncRx.NET (System.Reactive.Async) is still in **experimental preview/alpha** status.

To prevent destabilizing the core Streamix package and to avoid forcing a dependency on a preview library, all AsyncRx-related functionality is isolated within this project.

## Design Decisions

1. **Separate Assembly**: Interop is provided in a separate assembly (`Streamix.Extensions.dll`) so that users only take the dependency if they explicitly need it.
2. **Extension-Based API**: Methods like `ToAsyncObservable()`, `ToStream()`, and `ToSingle()` are implemented as extension methods to maintain a clean separation from the core `IStream<T>` and `ISingle<T>` interfaces.
3. **Push-Pull Bridge**: The bridge uses `System.Threading.Channels` for efficient and backpressure-aware conversion between the pull-based `IAsyncEnumerable<T>` used by Streamix and the push-based `IAsyncObservable<T>` used by AsyncRx.NET.

## Entity Framework Integration

Use `EfStream.From(...)` (or the `ToStream(...)` extension on a `DbContext` factory) to execute EF queries as Streamix streams:

```csharp
var activeCustomers = EfStream.From(
    ctx => ctx.Set<Customer>().Where(c => c.IsActive),
    () => new AppDbContext());
```

```csharp
var activeCustomers = (() => new AppDbContext()).ToStream(
    ctx => ctx.Set<Customer>().Where(c => c.IsActive));
```

Important semantics:

- Query construction and execution use the same `DbContext` instance per subscription.
- v1 execution materializes with `ToListAsync` before yielding items downstream.
- `Streamix.Extensions` includes EF Core as a transitive dependency by design.

## Learn More

- Overview and package map: [README.md](https://github.com/khurram-uworx/streamix/blob/main/README.md)
- Developer guide: [GETTING-STARTED.md](https://github.com/khurram-uworx/streamix/blob/main/GETTING-STARTED.md)
- Architecture and design notes: [ARCHITECTURE.md](https://github.com/khurram-uworx/streamix/blob/main/ARCHITECTURE.md)
- Repository: [github.com/khurram-uworx/streamix](https://github.com/khurram-uworx/streamix)
