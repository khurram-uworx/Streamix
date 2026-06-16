# AIDataEngg Example — Task Breakdown

> **IMPORTANT** This was the plan used for the execution by coding agents, things have changed since then, don't consider this as a source of truth, it's a historical artifact. The code as it is in the repo is the source of truth.

## Purpose

This document breaks the `examples/AIDataEngg` project into concrete,
assignable tasks for a coding agent. Each task produces a working,
verifiable increment.

## How the Pipeline Works (overview)

```
source.md ─→ parallel RSS fetch ─→ EfStream.From (buffered, dedup) ─→
  EfStream.FromStreamed (unprocessed items) ─→ parallel AI classify
    (Retry 3 + OnErrorReturn noise) ─→ ForEachAsync write-back to SQLite
```

## Coordination Notes

- Tasks 1–5 form a dependency chain and must execute in order.
- The config files (source.md, goal.md, signals.md) are already created.
- The example lives under `examples/AIDataEngg/`, all paths below are relative
  to repo root unless noted.
- The example targets `net10.0` (same as the main library).
- After each task, run `dotnet build --configuration Release` on the example
  project to catch build errors early. The full validation at the end is
  `dotnet build --configuration Release && dotnet test --configuration Release`.

---

## Task 1: Project scaffolding and dependency setup

### Priority

High

### Goal

Create the `examples/AIDataEngg/AIDataEngg.csproj` with all NuGet
dependencies and the project skeleton (`Program.cs`, `Data/`, `Models/`,
`Services/` folders + placeholder files).

### Scope

- Create `examples/AIDataEngg/AIDataEngg.csproj` targeting `net10.0`.
- Add these package references:
  - `Streamix` (project reference to `src/Streamix/Streamix.csproj`)
  - `Streamix.Extensions` (project reference to
    `src/Streamix.Extensions/Streamix.Extensions.csproj`)
  - `Microsoft.EntityFrameworkCore.Sqlite` (latest stable)
  - `Microsoft.Extensions.AI` (latest stable)
  - `Microsoft.Extensions.AI.OpenAI` (latest stable — provides
    `OpenAIChatClient` implementing `IChatClient`; works with any
    OpenAI-compatible endpoint including Foundry Local, Ollama, Azure OpenAI)
  - `System.ServiceModel.Syndication` (built-in RSS/Atom parser)
- Create empty placeholder files:
  - `Models/RssItem.cs`
  - `Models/ClassificationResult.cs`
  - `Models/ClassifiedRssItem.cs`
  - `Data/RssDbContext.cs`
  - `Services/RssFetcher.cs`
  - `Services/RssSha.cs`
  - `Services/RssClassifier.cs`
  - `Program.cs` (minimal `Console.WriteLine("Hello");`)
- Update `Streamix.slnx` to include the new example project (or document that
  it can be opened independently).

### Constraints

- Use project references (not NuGet) for `Streamix` and `Streamix.Extensions`
  so changes to the library are reflected immediately.
- The example must not introduce any external service dependencies.

### Acceptance criteria

- `dotnet build --configuration Release` succeeds from repo root.
- The example `.csproj` references all required packages.
- All placeholder files compile (even if they do nothing yet).

### Files likely involved

- `examples/AIDataEngg/AIDataEngg.csproj`
- `examples/AIDataEngg/Program.cs`
- `examples/AIDataEngg/Models/RssItem.cs`
- `examples/AIDataEngg/Models/ClassificationResult.cs`
- `examples/AIDataEngg/Models/ClassifiedRssItem.cs`
- `examples/AIDataEngg/Data/RssDbContext.cs`
- `examples/AIDataEngg/Services/RssFetcher.cs`
- `examples/AIDataEngg/Services/RssSha.cs`
- `examples/AIDataEngg/Services/RssClassifier.cs`
- `Streamix.slnx`

---

## Task 2: Models and EF Core data layer

### Priority

High

### Goal

Implement the EF Core entity models and `RssDbContext` for SQLite persistence
of RSS feed items and their classification results.

### Scope

- **`Models/RssItem.cs`** — EF entity representing a raw RSS feed entry.
  Properties:
  - `int Id` (PK, auto-increment)
  - `string FeedUrl` — the source feed URL
  - `string Title`
  - `string? Summary`
  - `string? Link` — original article URL
  - `DateTimeOffset Published` — published date from the feed
  - `string ContentHash` — SHA256 hex of (FeedUrl + Title + Published) for
    deduplication
  - `bool Processed` — `false` when first inserted, set to `true` after
    classification
  - `DateTimeOffset CreatedAt`
- **`Models/ClassificationResult.cs`** — record used for structured AI output:
  ```csharp
  public record ClassificationResult(
      string Signal,      // one of the signals from signals.md, or "noise"
      string? Reasoning,  // model's rationale
      bool IsNoise        // true when classified as noise
  );
  ```
- **`Models/ClassifiedRssItem.cs`** — EF entity storing the classification
  (linked to RssItem). Properties:
  - `int Id` (PK, auto-increment)
  - `int RssItemId` (FK → RssItem.Id)
  - `RssItem RssItem` (navigation property)
  - `string Signal`
  - `string? Reasoning`
  - `bool IsNoise`
  - `int AttemptCount` — how many times classification was attempted
  - `DateTimeOffset ClassifiedAt`
- **`Data/RssDbContext.cs`** — EF Core DbContext:
  - `DbSet<RssItem> RssItems`
  - `DbSet<ClassifiedRssItem> Classifications`
  - `OnConfiguring`: use `Sqlite("Data Source=aidataengg.db")`
  - `OnModelCreating`: configure indexes on `RssItem.ContentHash` (unique)
    and `RssItem.Processed` (for fast filtered queries).

### Constraints

- Enable nullable reference types (already enabled project-wide).
- `ContentHash` must have a unique index to prevent duplicate inserts.

### Acceptance criteria

- Models compile and are usable from a `DbContext`.
- A quick smoke test (can add via `dotnet test` or a console snippet) confirms
  that the unique hash constraint prevents duplicate inserts.

### Files likely involved

- `examples/AIDataEngg/Models/RssItem.cs`
- `examples/AIDataEngg/Models/ClassificationResult.cs`
- `examples/AIDataEngg/Models/ClassifiedRssItem.cs`
- `examples/AIDataEngg/Data/RssDbContext.cs`

---

## Task 3: RSS fetching service

### Priority

High

### Goal

Implement `RssFetcher` that takes a feed URL and returns an
`IAsyncEnumerable<RssItem>` for each entry in the RSS/Atom feed.

### Scope

- **`Services/RssFetcher.cs`** — static class with:
  ```csharp
  public static async IAsyncEnumerable<RssItem> FetchFeedAsync(
      string feedUrl,
      [EnumeratorCancellation] CancellationToken ct = default)
  ```
  - Uses `System.ServiceModel.Syndication.SyndicationFeed` (via
    `XmlReader.Create`) to parse the feed.
  - For each `SyndicationItem`, creates an `RssItem` with:
    - `FeedUrl = feedUrl`
    - `Title = item.Title?.Text ?? "(no title)"`
    - `Summary = item.Summary?.Text`
    - `Link = item.Links.FirstOrDefault()?.Uri?.ToString()`
    - `Published = item.PublishDate` (or `LastUpdatedTime` as fallback)
    - `ContentHash` computed via `RssSha.Compute(feedUrl, title, published)`
  - Yields items as they are parsed (streaming, not buffered).
  - Handles `SyndicationFeed.Load` exceptions by yielding a faulted
    iteration (exception propagates naturally).
- **`Services/RssSha.cs`** — static helper:
  ```csharp
  public static string Compute(string feedUrl, string title, DateTimeOffset published)
  ```
  - Computes SHA256 of `$"{feedUrl}|{title}|{published.Ticks}"` and returns
    the lowercase hex string.
- Update the `RssSha` hardcoded placeholder in `Services/RssSha.cs`.

### Constraints

- The method must be an async iterator (`IAsyncEnumerable<RssItem>`) so it
  integrates naturally with `Stream.From(...)`.
- Keep the method simple — no caching, no retry (that's Streamix's job).

### Acceptance criteria

- Given a known working RSS/Atom URL, the fetcher returns the expected items
  with populated fields.
- An invalid URL causes the enumerable to throw (test via
  `Record.ExceptionAsync`).

### Files likely involved

- `examples/AIDataEngg/Services/RssFetcher.cs`
- `examples/AIDataEngg/Services/RssSha.cs`

---

## Task 4: AI classification service

### Priority

High

### Goal

Implement `RssClassifier` that takes an `RssItem`, the business goal, and the
signal list, and returns a `ClassificationResult` via `IChatClient` using
structured output (`GetResponseAsync<T>`).

### Scope

- **`Services/RssClassifier.cs`** — static class with:
  ```csharp
  public static async Task<ClassificationResult> ClassifyAsync(
      RssItem item,
      string goal,
      string[] signals,
      IChatClient chatClient,
      CancellationToken ct = default)
  ```
  - Builds a system prompt instructing the model to classify the RSS item
    into one of the given signals or "noise".
  - The user message contains the item's `Title` and `Summary`.
  - Calls `chatClient.GetResponseAsync<ClassificationResult>(messages, ...)`
    to get a structured response.
  - If `GetResponseAsync` throws (malformed response, network error, model
    refuses), the exception propagates — `Retry(3)` in the pipeline handles
    it.
- The system prompt should:
  - State the business goal so the model has context.
  - List the available signals.
  - Instruct the model to respond ONLY with the matching signal or "noise".
  - Ask for brief reasoning.
  - Warn the model that it MUST return valid JSON matching
    `ClassificationResult` schema.

### Constraints

- Do **not** configure or create the `IChatClient` in this class — it is
  injected so the example works with any provider (Foundry Local, Ollama,
  Azure OpenAI, etc.).
- Keep the prompt in a `const string` or a static readonly field so it is
  easy to tweak.

### Acceptance criteria

- Given a mock `IChatClient`, the method returns a correctly deserialized
  `ClassificationResult`.
- When the client throws, the exception propagates unmodified.

### Files likely involved

- `examples/AIDataEngg/Services/RssClassifier.cs`

---

## Task 5: Pipeline orchestration (Program.cs)

### Priority

High

### Goal

Wire the full pipeline in `Program.cs` using `ScopedAsync` and Streamix
operators. Connect everything: read config, fetch RSS, dedup-save, read back
unprocessed, classify, write results.

### Scope

- **`Program.cs`** — full orchestration:

  1. **Read config files**:
     - `source.md` — parse `- <url>` lines into `string[]`.
     - `goal.md` — read as a single string.
     - `signals.md` — parse `- <signal>` lines into `string[]`.

  2. **Set up EF Core**:
     - `Func<DbContext>` factory:
       ```csharp
       var dbFactory = () => new RssDbContext();
       ```
     - EnsureCreated on startup so the SQLite DB + schema exist.

  3. **Set up IChatClient**:
     - Read endpoint + model + apiKey from environment variables
       (`AI_ENDPOINT`, `AI_MODEL`, `AI_API_KEY`) with sensible defaults
       for local development (e.g., `http://localhost:11434/v1` for Ollama,
       model `phi4-mini`).
     - Create `OpenAIChatClient(endpoint, apiKey)` with the model.
     - Use `IChatClient` as the abstraction.

  4. **Streamix pipeline** (inside `Stream.ScopedAsync`):
     ```csharp
     await Stream.ScopedAsync(async scope =>
     {
         // Stage 1: parallel RSS fetch
         var rssItems = Stream
             .From(feedUrls)
             .FlatMap(url => RssFetcher.FetchFeedAsync(url), maxConcurrency: 4);

         // Stage 2: save new items (buffered, dedup)
         var saved = rssItems
             .Filter(item => IsNotDuplicate(item.ContentHash, dbFactory))
             .DoOnNext(item => InsertItem(item, dbFactory));

         // Stage 3: read back unprocessed (streamed)
         var unprocessed = EfStream
             .FromStreamed<MyEntity>(ctx => ctx.RssItems.Where(r => !r.Processed), dbFactory,
                 name: "read-unprocessed");

         // Stage 4: parallel AI classification with retry
         var classified = unprocessed
             .FlatMap(item =>
                 Stream
                     .From(ct => RssClassifier.ClassifyAsync(item, goal, signals, chatClient, ct))
                     .Retry(3)
                     .OnErrorReturn(new ClassificationResult("noise",
                         "All 3 classification attempts failed.", true)),
                 maxConcurrency: 4);

         // Stage 5: write results + mark as processed
         await classified
             .ForEachAsync(result => SaveClassification(item, result, dbFactory));
     });
     ```

  5. **Helper methods** (local functions in Program.cs or a small
     `PipelineHelpers` static class):
     - `IsNotDuplicate(string hash, Func<DbContext> factory)` — returns
       `true` if no item with that hash exists.
     - `InsertItem(RssItem item, Func<DbContext> factory)` — inserts and
       saves.
     - `SaveClassification(RssItem item, ClassificationResult result,
       Func<DbContext> factory)` — inserts `ClassifiedRssItem`, sets
       `item.Processed = true`, saves.

### Constraints

- "Don't let EF query leak into the pipeline" — use `EfStream.FromStreamed`
  for the read-back stage rather than `ToListAsync` in a helper.
- `IsNotDuplicate` must hit the DB per item (not cache in memory) since
  items arrive concurrently. This is simple with a `AnyAsync` call but
  ensure the context is created per call.
- The pipeline must handle cancellation via `scope.CancellationToken`.
- Log pipeline progress to console at each stage start/end using
  `.DoOnNext`/`.DoOnComplete` or `.Checkpoint`.

### Acceptance criteria

- The project compiles and runs end-to-end (requires a running AI endpoint).
- The pipeline prints stage progress to console.
- New RSS items are persisted with `Processed = false`.
- After classification, items have `Processed = true` and a corresponding
  `ClassifiedRssItem` row exists.
- If classification fails 3 times for an item, it is classified as "noise"
  with `AttemptCount = 3` and `Processed = true`.

### Files likely involved

- `examples/AIDataEngg/Program.cs`

---

## Task 6: Build and smoke test

### Priority

Medium

### Goal

Verify the entire example builds, the pipeline logic is sound, and that the
project integrates cleanly with the rest of the Streamix solution.

### Scope

- Run `dotnet build --configuration Release` from repo root.
- Fix any build errors related to the example.
- Run `dotnet test --configuration Release` to confirm no regressions in
  the main library.

### Acceptance criteria

- Clean release build with no warnings introduced.
- All existing tests pass.

### Files likely involved

- Any `.cs` files that need minor fixes to compile.

---

## Suggested Agent Handout

### Batch A (sequential — must run in order)

- Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6

### Batch B (can run independently after Task 3)

- [none — all pipeline tasks depend on previous scaffolding]

## Final Checklist

- [x] Config files (source.md, goal.md, signals.md) created
- [x] Task 1: project scaffolding done
- [x] Task 2: models and EF data layer done
- [x] Task 3: RSS fetching service done
- [x] Task 4: AI classification service done
- [x] Task 5: pipeline orchestration done
- [x] Task 6: build + smoke test passes
