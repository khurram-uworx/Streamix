# Concurrency

## Purpose

This file is for future concurrency follow-up only.

Settled public contract and current guidance now live in:

- `README.md`
- `GETTING-STARTED.md`
- `ARCHITECTURE.md`

## Future Follow-Up Candidates

- keep supervision and bounded-concurrency wording aligned as new operators or examples are added
- expand the concurrency verification matrix only when new operator categories or boundary semantics are introduced
- re-evaluate supervision or execution-graph diagnostics only if a concrete debugging, maintainability, or test-audit gap appears
- treat alternative fault policies or exception aggregation as new contract work, not routine cleanup
- revisit channel-boundary documentation only if `PipeThroughChannel(...)`, `RunOnChannel(...)`, or `TeeToChannel(...)` semantics materially change
