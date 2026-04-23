# Time Series 2

## Purpose

This file is for next-release time-series follow-up only.

Settled current contract and shipped guidance now live in:

- `README.md`
- `ARCHITECTURE.md`
- `docs/TIMESERIES.md`

Use this file to carry forward only the work that remains relevant after the current release is finalized.

## Carry-Forward Decisions

- Event-time windowing with tumbling and sliding windows is already part of the shipped Streamix story.
- Watermark-aware event-time behavior is already implemented and should remain additive rather than changing the meaning of existing ordered usage.
- Session windows are already implemented and should be treated as part of the settled current time-series surface.
- Time-based joins remain deferred and should continue to be treated as a separate feature slice rather than an extension of existing windows.
- Additional time-based operators are the best candidate for the next release, provided the API stays narrow and semantics stay explicit.

## Next-Release Priority

1. Task 5: prioritize and implement one narrow additional time-based operator
2. Task 4: evaluate time-based joins for a later release after Task 5 settles

## Ready Task

## Task 5: Prioritize Additional Time-Based Operators

### Priority

High

### Release recommendation

In scope for the next release.

### Goal

Select and ship one small, well-defined time-based operator that extends the current time-series surface without introducing API sprawl.

### Why this is the next release candidate

- It fits the existing README roadmap item for additional time-based operators.
- It is materially smaller and lower-risk than time-based joins.
- It can build on existing Streamix timing, cancellation, and backpressure patterns without introducing a second major event-correlation feature family.

### Recommended first operator

`BufferByTime`

### Why `BufferByTime` is recommended first

- It has a clear user value for batching and burst smoothing.
- It aligns naturally with Streamix's pull-first and backpressure-aware design.
- Its semantics are easier to define and test cleanly than `Sample`.
- It does not require the bilateral state and completion rules that make joins expensive.

### Recommended deferral

- Defer `Sample` until after `BufferByTime`, unless a concrete product scenario proves `Sample` is more valuable.
- Defer all join work to Task 4 in a later release.

### Agent handoff

This task is ready to assign to a coding agent after the semantic contract below is accepted.

The agent should treat this as a narrow feature slice:

- implement `BufferByTime` only
- do not add multiple new operators in the same change
- do not broaden the public API with convenience overloads unless they are necessary for a coherent minimal contract
- preserve existing Streamix cancellation, exception, and backpressure behavior

### Proposed v1 semantic contract for `BufferByTime`

- `BufferByTime` is a processing-time operator, not an event-time operator.
- It groups source items observed during a fixed processing-time interval into a buffered collection.
- Each emitted buffer preserves source ordering.
- Empty buffers are not emitted.
- On upstream completion, any non-empty in-progress buffer is emitted once before completion.
- On upstream failure, the operator propagates the exception and does not emit a synthetic final buffer after the fault.
- On cancellation, the operator stops promptly and does not emit a cancellation-time flush.
- The first buffer window starts when enumeration begins.
- A testable clock should be supported if the current operator/testing patterns make that practical; otherwise the initial implementation may use the existing timing model used elsewhere in the repo, provided tests remain deterministic.

### Suggested minimal API shape

- Add one public operator with a small surface, for example:
  `BufferByTime(TimeSpan interval)`
- Add at most one overload only if needed for testability or consistency with current timing operators.

### Constraints

- Do not mix processing-time batching with watermark or event-time semantics in the first version.
- Do not emit empty buffers.
- Do not weaken backpressure behavior or introduce unbounded internal growth.
- Do not turn this task into a general scheduler/timer abstraction expansion unless required by the implementation.
- Do not update the README until the operator name and contract are final.

### Suggested implementation path

1. Confirm the final v1 contract in `ARCHITECTURE.md` or this file before coding.
2. Implement a single `BufferByTime` operator in the core library.
3. Add focused tests for timing behavior, completion flush, cancellation, and exception propagation.
4. Add a backpressure-oriented test if the implementation crosses a channel boundary or uses timer-driven coordination.
5. Update `README.md` and `ARCHITECTURE.md` only after implementation and tests settle.

### Acceptance criteria

- one additional time-based operator is selected and implemented
- the operator has an explicit documented semantic contract
- tests cover normal behavior, timing boundaries, completion flush, cancellation, and failure propagation
- the public API remains small and coherent
- README and architecture docs describe only the behavior that actually ships

### Files likely involved

- `src/Streamix`
- `src/Streamix.Tests`
- `ARCHITECTURE.md`
- `README.md`

## Deferred Follow-Up

## Task 4: Evaluate Time-Based Joins

- Keep deferred to the release after the next one unless a concrete scenario creates stronger priority than additional operators.
- Treat joins as a separate design/problem space with their own contract, tests, and likely dedicated task breakdown.
- Do not fold joins into `WindowByTime`, `WindowBySession`, or `BufferByTime`.

## Future Follow-Up Candidates

- `Sample` after `BufferByTime`, if a clear scenario justifies it
- other time-based operators only when they have concrete semantics and user value
- a dedicated task breakdown doc for time-based joins when Task 4 becomes active
