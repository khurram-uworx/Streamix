# Structured Concurrency Design

## Overview

Streamix aims to provide a structured concurrency model that ensures concurrent operations have well-defined lifetimes, clear parent-child relationships, and predictable failure/cancellation semantics.

The core abstraction is the **Scope**, which manages a set of concurrent tasks. A scope does not complete until all its child tasks have finished.

## Public API

### Entry Point

```csharp
namespace Streamix;

public static partial class Stream
{
    /// <summary>
    /// Executes an asynchronous action within a structured concurrency scope.
    /// The scope waits for all spawned tasks to complete before returning.
    /// If any task fails, the scope is cancelled, and the error is propagated.
    /// </summary>
    public static Task ScopedAsync(Func<IStreamScope, Task> action, CancellationToken cancellationToken = default);
}
```

### Scope Interface

```csharp
namespace Streamix;

public interface IStreamScope
{
    /// <summary>
    /// Gets a cancellation token that is cancelled when the scope is cancelled or fails.
    /// This token is linked to the parent cancellation token passed to ScopedAsync.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Spawns a concurrent task within the scope.
    /// The scope will wait for this task to complete.
    /// </summary>
    void Run(Func<CancellationToken, Task> work);
}
```

## Semantics

### Lifetime
- A scope starts when `ScopedAsync` is called.
- A scope remains active as long as the provided `action` is running OR any task spawned via `Run` is still active.
- `ScopedAsync` returns a `Task` that completes only when all work (the main action and all spawned tasks) has settled.

### Cancellation
- If the parent `CancellationToken` provided to `ScopedAsync` is cancelled, the scope's `CancellationToken` is also cancelled.
- All child tasks should monitor `scope.CancellationToken` and respond to cancellation.

### Failure
- **Fail-Fast**: If any child task or the main action throws an exception, the scope is immediately marked as failing.
- The scope's `CancellationToken` is cancelled to signal other tasks to stop.
- The scope waits for all remaining tasks to finish (successful, cancelled, or failed).
- After all tasks have settled, the original exception is rethrown. If multiple exceptions occurred, they may be aggregated.

### Resource Safety
- Scopes can be nested. A child scope is treated as a single task within the parent scope.
- `IStreamScope` does not implement `IDisposable`; its lifetime is managed by the `ScopedAsync` block.

## Examples

### Concurrent Work

```csharp
await Stream.ScopedAsync(async scope =>
{
    scope.Run(async ct =>
    {
        await Task.Delay(100, ct);
        Console.WriteLine("Task 1 done");
    });

    scope.Run(async ct =>
    {
        await Task.Delay(50, ct);
        Console.WriteLine("Task 2 done");
    });

    Console.WriteLine("Main action done");
});
// Reaches here only after Task 1 and Task 2 complete.
```

### Error Propagation

```csharp
try
{
    await Stream.ScopedAsync(async scope =>
    {
        scope.Run(async ct =>
        {
            await Task.Delay(10, ct);
            throw new InvalidOperationException("Oops");
        });

        scope.Run(async ct =>
        {
            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Task 2 was cancelled due to Task 1 failure");
            }
        });
    });
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Caught: {ex.Message}");
}
```
