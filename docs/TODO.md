# Investigation and Fix for Issue #155

## Problem
When `PipeThroughChannel(capacity, ChannelBackpressureMode.Fail)` is applied to a `Flux.From(IEnumerable<T>)` source, the `BackpressureException` is silently swallowed. The consumer sees the stream end cleanly instead of receiving the exception.

This does **not** happen with `Flux.Range` (which uses `AsyncEnumerable.Range`).

## Root Cause Analysis
The issue is in the `ReadAllSupervisedAsync` method in `src/Streamix/Implementations/ScopeHelper.cs`. 

When a `BackpressureException` is thrown in `WriteAsync` (due to channel being full in Fail mode):
1. The exception is caught by `scope.Run`'s try/catch block
2. `channel.Writer.TryComplete(ex)` is called to mark the writer as completed with the exception
3. The exception is re-thrown and caught by `scope.Run`'s outer try/catch
4. `RecordException(ex)` is called, setting `scope.firstException` and making `scope.IsFaulted` return `true`
5. Back in `PipeThroughChannel`, the `await foreach` loop calls `ReadAllSupervisedAsync`
6. `ReadAllSupervisedAsync` checks `if (scope.IsFaulted) break;` **before** attempting to read any items from the channel
7. This causes the method to break immediately without yielding any items that were already written to the channel
8. The `finally` block of `PipeThroughChannel` then executes `await ScopeHelper.FinalizeScopeAsync(scope)`
9. `FinalizeScopeAsync` calls `scope.ThrowIfFailed()` which throws the original `BackpressureException`
10. However, this exception is thrown in the `finally` block of an async iterator that has already completed normally, causing it to be silently swallowed by the .NET runtime

## Evidence Map
- **PipeThroughChannel Method**: `src/Streamix/Extensions/FluxExtensions.cs:line 1702-1703`
- **ReadAllSupervisedAsync Method**: `src/Streamix/Implementations/ScopeHelper.cs:line 8-34` 
- **WriteAsync Method**: `src/Streamix/ChannelExecution.cs:line 74-91`
- **FinalizeScopeAsync Method**: `src/Streamix/Implementations/ScopeHelper.cs:line 37-55`
- **FluxScope.RecordException Method**: `src/Streamix/Implementations/FluxScope.cs:line 34-49`
- **FluxScope.ThrowIfFailed Method**: `src/Streamix/Implementations/FluxScope.cs:line 158-172`

## Blast Radius
- **Files Affected**: 
  - `src/Streamix/Implementations/ScopeHelper.cs` (primary fix)
  - Potentially any callers of `ReadAllSupervisedAsync` (should maintain backward compatibility)
- **Downstream Callers**: 
  - `PipeThroughChannel` (primary usage)
  - `RunOnChannel` 
  - Any other methods using `ReadAllSupervisedAsync`
- **Test Coverage**: Existing tests in `tests/Streamix.Tests/FluxTests.cs` should verify the fix

## Solution
Modify `ReadAllSupervisedAsync` to attempt reading available items from the channel **before** checking if the scope has faulted. This ensures that items written to the channel before the fault occurred are still yielded to the consumer.

The fix involves reordering the logic in `ReadAllSupervisedAsync`:
1. First, attempt to read all currently available items from the channel
2. If items were read, yield them and continue looping to see if more are immediately available
3. If no items are immediately available, then check if the scope has faulted
4. If scope is faulted, break (don't wait for more items)
5. Otherwise, wait for either new items or channel completion

## Technical Constraints
- Must maintain backward compatibility for existing usage of `ReadAllSupervisedAsync`
- Should not change the behavior for successful scenarios
- Must ensure exceptions still propagate correctly through the normal flow when possible
- The fix should be minimal and focused

## Verification Steps
1. Create a test that reproduces the exact issue from the GitHub issue
2. Verify that `BackpressureException` now propagates correctly for `Flux.From(IEnumerable<T>).PipeThroughChannel(..., Fail)`
3. Ensure existing tests still pass
4. Verify that `Flux.Range(..., Fail)` behavior remains unchanged (should continue to work)
5. Test edge cases like empty sources, single-item sources, etc.

## Planned Commit List
1. `docs: plan investigation and fix for issue #155 in TODO.md`
2. `fix: modify ReadAllSupervisedAsync to read channel items before checking scope fault`
3. `test: add test case reproducing issue #155 scenario`
4. `test: verify existing PipeThroughChannel tests still pass`

## GitHub issues log

- [ ] #155 — PipeThroughChannel swallows BackpressureException when source is AsyncEnumerable.FromEnumerable (in progress)