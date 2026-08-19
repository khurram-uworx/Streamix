# AIDataEngg Plan 3 — TODO

> Items identified during the post-implementation review against `AIDataEngg-Plan3.md`.
> Numbered A–G by priority. Add new items as H, I, … to avoid renumbering.

---

## Task A: Config Editor Page

### Priority

High

### Goal

A Razor Page at `/` (or `/Config`) that lets users edit RSS feeds, goal, signals, and prompt before running the pipeline — same as editing the 4 markdown files by hand, but from the browser.

### Why this exists

The plan calls for it (Task 4). Currently Index is a dashboard with no config editing. Users must edit `.md` files on disk to change feeds/signals/prompt.

### Scope

- Add `Pages/Config.cshtml` + `Config.cshtml.cs` (or repurpose Index with a toggle)
- 4 textareas: FeedSources, Goal, Signals, Prompt — pre-filled from `ConfigLoader`
- POST handler saves back via `ConfigLoader`
- TempData success banner on save
- Link from sidebar nav

### Constraints

- ConfigLoader already reads/writes markdown files — no new persistence needed
- Single-user, container-friendly — works with writable volume mount

### Acceptance criteria

- Navigating to the page shows 4 textareas with current file content
- Editing + saving persists to disk (verify by reload)
- Success banner shown after save
- Sidebar has a nav link to the page

### Files likely involved

- `examples/AIDataEngg/AIDataEngg.Web/Pages/Config.cshtml`
- `examples/AIDataEngg/AIDataEngg.Web/Pages/Config.cshtml.cs`
- `examples/AIDataEngg/AIDataEngg.Web/Pages/_Layout.cshtml`
- `examples/AIDataEngg/Streamix.AIDataEngg/Services/ConfigLoader.cs`

---

## Task B: Item Detail Page ✅

### Priority

High

### Status

Completed. Implemented `Pages/Item.cshtml` + `Item.cshtml.cs` with route `/Item/{id}`. Includes:
- Detail header with title, original link, feed name, published/classified dates
- Classification card: signal badge, noise badge, attempt count, hallucinated signal, reasoning text
- Summary section: full RSS summary
- Actions card: Reclassify dropdown (all signals from config), Mark Not Noise (when IsNoise), Retry (when failed), Delete
- "More Like This" panel: similar items via `MoreLikeAsync` with cosine similarity scores
- 404 for non-existent id
- All three list pages (Signals, Noise, Bounced) wired to navigate on row click
- Action buttons use `event.stopPropagation()` to prevent row-click navigation

---

## Task C: Expand IFeedbackService Interface

### Priority

Medium

### Goal

Align the `IFeedbackService` interface with the plan's signature — add `MoreLikeAsync`, `GetSignalCountsAsync`, `GetNoiseCountAsync`, `GetFailedCountAsync`, and `MarkNotNoiseAsync` / `RetryFailedAsync` as explicit methods.

### Why this exists

Current interface is simpler than the plan. Some planned methods are missing, and the `ReclassifyAsync(bool isNoise)` pattern conflates reclassification with noise-toggling.

### Scope

- Add to `IFeedbackService`:
  - `MoreLikeAsync(int classifiedId, int top = 6, CancellationToken ct = default)`
  - `GetSignalCountsAsync(CancellationToken ct = default)` returning `Dictionary<string, int>`
  - `GetNoiseCountAsync(CancellationToken ct = default)`
  - `GetFailedCountAsync(CancellationToken ct = default)`
  - `MarkNotNoiseAsync(int classifiedId, CancellationToken ct = default)`
  - `RetryFailedAsync(int classifiedId, CancellationToken ct = default)`
- Implement in `FeedbackService`
- Update hub methods and page models to use new methods where appropriate

### Acceptance criteria

- All new methods have implementations in `FeedbackService`
- Existing tests still pass
- Hub exposes `MoreLike` method

### Files likely involved

- `examples/AIDataEngg/Streamix.AIDataEngg/Services/IFeedbackService.cs`
- `examples/AIDataEngg/Streamix.AIDataEngg/Services/FeedbackService.cs`
- `examples/AIDataEngg/AIDataEngg.Web/Hubs/PipelineHub.cs`

---

## Task D: Refactor Console Program.cs to Use PipelineOrchestrator

### Priority

Low

### Goal

Replace the 266-line inline pipeline in `Program.cs` with the library's `PipelineOrchestrator`, reducing it to ~30 lines — matching the plan's Task 2 scope.

### Why this exists

The plan says the console app should be a thin shell (~30 lines) over the library. Currently it keeps the full Streamix-operator pipeline for tutorial value. This task is optional — the current code works, but diverges from the plan.

### Decision required

Keep the inline pipeline as a tutorial demonstration of Streamix operators, or replace with `PipelineOrchestrator` for consistency. If kept, document the reason in the console app's README.

### Scope (if accepted)

- Replace inline pipeline with `PipelineOrchestrator.RunAsync`
- Remove `Helper.cs` (logic now in orchestrator)
- Keep `--smoke`, `--config-check`, `--no-feedback` flags
- Progress handler: `Console.WriteLine` for each `PipelineEvent`

### Acceptance criteria

- `dotnet run --project examples/AIDataEngg/AIDataEngg -- --config-check` works
- `dotnet run --project examples/AIDataEngg/AIDataEngg -- --smoke` works
- Pipeline produces same classification output as before
- No duplicate types between console and library

### Files likely involved

- `examples/AIDataEngg/AIDataEngg/Program.cs`
- `examples/AIDataEngg/AIDataEngg/Helper.cs`

---

## Task E: Extract Shared Partial Views

### Priority

Low

### Goal

Extract the sidebar navigation from `_Layout.cshtml` into `_SignalNav.cshtml` partial, and create `_ItemList.cshtml` partial for consistent item row rendering across Signals/Noise/Bounced pages.

### Why this exists

The plan calls for both partials. Currently the nav is inlined in the layout, and item rows are duplicated across 3 pages.

### Scope

- `Pages/Shared/_SignalNav.cshtml` — sidebar folder list with counts
- `Pages/Shared/_ItemList.cshtml` — consistent item row template
- Update `_Layout.cshtml` to use `_SignalNav`
- Update Noise, Bounced, Signals pages to use `_ItemList`

### Acceptance criteria

- Sidebar renders identically before and after extraction
- Item rows look the same on Signals, Noise, and Bounced pages
- No visual regressions

### Files likely involved

- `examples/AIDataEngg/AIDataEngg.Web/Pages/_Layout.cshtml`
- `examples/AIDataEngg/AIDataEngg.Web/Pages/Shared/_SignalNav.cshtml`
- `examples/AIDataEngg/AIDataEngg.Web/Pages/Shared/_ItemList.cshtml`
- `examples/AIDataEngg/AIDataEngg.Web/Pages/Signals.cshtml`
- `examples/AIDataEngg/AIDataEngg.Web/Pages/Noise.cshtml`
- `examples/AIDataEngg/AIDataEngg.Web/Pages/Bounced.cshtml`

---

## Suggested Execution Order

1. **Task A** (Config editor) — highest user-facing gap, no dependencies
2. **Task B** (Item detail) — core feedback UI, depends on Task C if MoreLike is needed
3. **Task C** (Expand IFeedbackService) — prerequisite for Task B's MoreLike, can run alongside A
4. **Task D** (Console slim) — independent, optional
5. **Task E** (Partial views) — independent, cosmetic

## Coordination Notes

- Tasks A and C can run in parallel (different files).
- Task B depends on Task C (MoreLikeAsync needs to exist first).
- Tasks D and E are independent of all others.
- No shared files between tasks A–E except `IFeedbackService.cs` (Task C touches it, Task B reads it).
