# AIDataEngg → Shared Library + ASP.NET Host: Assessment

> Written: 2026-06-17
> Based on full source analysis of `examples/AIDataEngg` (Models, Services, Data, Helper, Program.cs, configs)

---

## Current Architecture (Console App)

```
examples/AIDataEngg/
  AIDataEngg.csproj        — Exe, net10.0, pins 6 NuGet packages
  Program.cs                — 265 lines: config load → 6-stage pipeline → interactive feedback loop
  Helper.cs                 — EmbeddedRssItem record, ClassifyAndValidateAsync, EnsureSchemaAsync,
                              RestoreVectorStateAsync, PersistAndIndexAsync
  Models/
    RssItem.cs              — EF entity (Id, FeedUrl, FeedName, Title, Summary, Link, Published, ContentHash, Processed)
    ClassificationResult.cs — record { Signal, Reasoning, IsNoise, HallucinatedSignal? }
    ClassifiedRssItem.cs    — EF entity (Id, RssItemId, Signal, Reasoning, IsNoise, AttemptCount, HallucinatedSignal, ClassifiedAt, Embedding)
    VectorIndexEntry.cs     — Vector store entry (Id, RssItemId, Signal, Title, Summary, Embedding); also EmbeddingDefaults
  Services/
    RssFetcher.cs           — FetchFeedAsync → IAsyncEnumerable<RssItem> from SyndicationFeed
    EmbeddingService.cs     — GenerateAsync(RssItem|string) → ValueTask<ReadOnlyMemory<float>>
    RssClassifier.cs        — ClassifyAsync(IChatClient, RssItem, systemPrompt) → ClassificationResult
    VectorClassifier.cs     — Hybrid NN+LLM classifier; also ClassificationDecision, NeighborStats, ClassificationSource
    CategoryCentroidTracker.cs — Running-mean centroid per signal, thread-safe
    EmbeddingSerializer.cs  — ToBytes / FromBytes round-trip
    VectorStoreProvider.cs  — GetOrCreateCollectionAsync → InMemoryVectorStore
    UserFeedbackService.cs  — Interactive console loop: like, hide, morelike, recent
    VectorStoreSmoke.cs     — Standalone smoke test (--smoke)
  Data/
    RssDbContext.cs         — SQLite via EF Core, RssItems + Classifications tables
  configs/
    source.md               — RSS feed list (name | url lines)
    goal.md                 — Business goal text
    signals.md              — Allowed signal names
    prompt.md               — System prompt template (with {goalText} / {signalsText} placeholders)
```

**Six-stage pipeline** (all in `Program.cs`):

1. Parallel RSS fetch (max 4 concurrency)
2. Dedup + save to SQLite
3. Count unprocessed items
4. Generate embeddings (max 4 concurrency)
5. Hybrid classify: vector NN → LLM fallback (serial, max 1); retry 3x on hallucinated signals
6. Summary + interactive feedback loop

**The interactive feedback loop** (`UserFeedbackService`):
- Reads commands from `Console.In` via Streamix `Flux.From(ReadCommandsAsync)`
- Commands: `recent`, `like <id>`, `hide <id>`, `morelike <id>`, `help`, `quit`
- Like/hide tracked in-memory only (not persisted)

---

## What Moves to the Shared Library

### 1. New Project: `src/Streamix.AIDataEngg/`

**Type**: `dotnet new classlib` — net10.0, no Exe output.

**NuGet dependencies** (moved from current `.csproj`):
- `Microsoft.EntityFrameworkCore.Sqlite` — DbContext (could be made provider-agnostic later)
- `Microsoft.Extensions.AI` & `Microsoft.Extensions.AI.OpenAI` — IChatClient, IEmbeddingGenerator
- `Microsoft.SemanticKernel.Connectors.InMemory` — InMemoryVectorStore
- `System.Numerics.Tensors` — TensorPrimitives (centroid math)
- `System.ServiceModel.Syndication` — RSS parsing
- Add: `Microsoft.Extensions.DependencyInjection` — for DI registration extension method
- Add: `Microsoft.Extensions.Configuration` — for config loading

**Files to move** (verbatim or near-verbatim):

| Source | Destination | Changes needed |
|---|---|---|
| `Models/RssItem.cs` | `Models/RssItem.cs` | None |
| `Models/ClassificationResult.cs` | `Models/ClassificationResult.cs` | None |
| `Models/ClassifiedRssItem.cs` | `Models/ClassifiedRssItem.cs` | None |
| `Models/VectorIndexEntry.cs` | `Models/VectorIndexEntry.cs` | None (includes EmbeddingDefaults) |
| `Services/RssFetcher.cs` | `Services/RssFetcher.cs` | Change namespace |
| `Services/EmbeddingService.cs` | `Services/EmbeddingService.cs` | Change namespace |
| `Services/RssClassifier.cs` | `Services/RssClassifier.cs` | Change namespace |
| `Services/VectorClassifier.cs` | `Services/VectorClassifier.cs` | Change namespace (incl. ClassificationDecision, NeighborStats, ClassificationSource) |
| `Services/CategoryCentroidTracker.cs` | `Services/CategoryCentroidTracker.cs` | Change namespace |
| `Services/EmbeddingSerializer.cs` | `Services/EmbeddingSerializer.cs` | Change namespace |
| `Services/VectorStoreProvider.cs` | `Services/VectorStoreProvider.cs` | Change namespace |
| `Data/RssDbContext.cs` | `Data/RssDbContext.cs` | Change namespace; accept connection string via options |
| `Helper.cs` | `Helper.cs` | Change namespace |

**New files to create:**

| File | Purpose |
|---|---|
| `PipelineOrchestrator.cs` | Encapsulates the 6-stage pipeline (currently inline in Program.cs). Accepts `IProgress<PipelineProgress>` or an `IObserver<PipelineEvent>` so both console and ASP.NET can observe without library coupling. |
| `ConfigLoader.cs` | Reads `source.md`, `goal.md`, `signals.md`, `prompt.md` from a config directory. Returns a `PipelineConfig` record. |
| `IFeedbackService.cs` | Interface for feedback actions: `ListBySignalAsync(signal)`, `ListNoiseAsync()`, `ListFailedAsync()`, `ReclassifyAsync(id, newSignal)`, `MarkNotNoiseAsync(id)`, `RetryFailedAsync(id)`, `MoreLikeAsync(id)`. |
| `PipelineConfig.cs` | Record: `FeedSources`, `Goal`, `Signals`, `ValidSignals`, `SystemPrompt`. |
| `PipelineEvent.cs` | Union/discriminated records for SignalR progress: `StageChanged`, `ItemProcessed`, `PipelineComplete`, `PipelineError`. |
| `ServiceCollectionExtensions.cs` | `AddAIDataEngg(this IServiceCollection, IConfiguration)` — wires DbContext factory, services, config loader. |

**`PipelineOrchestrator` sketch:**

```csharp
public class PipelineOrchestrator
{
    public PipelineOrchestrator(
        ConfigLoader configLoader,
        IDbContextFactory<RssDbContext> dbFactory,
        EmbeddingService embeddingService,
        VectorStoreCollection<int, VectorIndexEntry> vectorCollection,
        CategoryCentroidTracker centroids,
        VectorClassifier classifier,
        IChatClient chatClient);

    public async Task RunPipelineAsync(
        IProgress<PipelineEvent>? progress = null,
        CancellationToken ct = default);
}
```

The Console host calls `RunPipelineAsync` with a `Progress<PipelineEvent>` that writes to `Console.WriteLine`.
The ASP.NET host calls it with a progress that pushes to SignalR clients.

**`IFeedbackService` sketch:**

```csharp
public interface IFeedbackService
{
    Task<List<ClassifiedRssItem>> ListBySignalAsync(string signal, int limit = 50, CancellationToken ct = default);
    Task<List<ClassifiedRssItem>> ListNoiseAsync(int limit = 50, CancellationToken ct = default);
    Task<List<ClassifiedRssItem>> ListFailedAsync(int limit = 50, CancellationToken ct = default);
    Task<List<ClassifiedRssItem>> ListRecentAsync(int limit = 20, CancellationToken ct = default);
    Task ReclassifyAsync(int classifiedId, string newSignal, CancellationToken ct = default);
    Task MarkNotNoiseAsync(int classifiedId, CancellationToken ct = default);
    Task RetryFailedAsync(int classifiedId, CancellationToken ct = default);
    Task<List<(ClassifiedRssItem Item, double Score)>> MoreLikeAsync(int classifiedId, int top = 6, CancellationToken ct = default);
    Task<ClassificationStats> GetStatsAsync(CancellationToken ct = default);
}
```

A `FeedbackService` class implements this using `IDbContextFactory<RssDbContext>` + `VectorStoreCollection`.

---

## Console App: Slimmed Down

`examples/AIDataEngg/` becomes a thin host:

```
examples/AIDataEngg/
  AIDataEngg.csproj        — references Streamix.AIDataEngg, Streamix, Streamix.Extensions
  Program.cs               — ~30-50 lines: DI setup, config load, RunPipelineAsync, optional feedback
  configs/                 — unchanged (source.md, goal.md, signals.md, prompt.md)
  README.md                — update to reflect library usage
```

`Program.cs` becomes:

```csharp
using Streamix.AIDataEngg;
// ... DI setup ...

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, svc) => svc.AddAIDataEngg(ctx.Configuration))
    .Build();

var orchestrator = host.Services.GetRequiredService<PipelineOrchestrator>();

// progress handler writes to console
var progress = new Progress<PipelineEvent>(e => Console.WriteLine(e switch {
    StageChanged s => $"[{s.Stage}] {s.Message}",
    ItemProcessed i => $"  [{i.Index}/{i.Total}] {i.Title} -> {i.Signal}",
    PipelineComplete => "Pipeline complete.",
    PipelineError err => $"ERROR: {err.Message}",
    _ => e.ToString()
}));

await orchestrator.RunPipelineAsync(progress);

// interactive feedback loop (optional, --no-feedback to skip)
if (!args.Contains("--no-feedback"))
{
    var feedback = host.Services.GetRequiredService<IFeedbackService>();
    await RunInteractiveFeedbackAsync(feedback, Console.In, Console.Out);
}
```

---

## New ASP.NET Host: `examples/AIDataEngg.Web/`

### Project Setup

```
dotnet new web --name AIDataEngg.Web
cd AIDataEngg.Web
dotnet add reference ../../src/Streamix.AIDataEngg/Streamix.AIDataEngg.csproj
dotnet add reference ../../src/Streamix.AspNetCore/Streamix.AspNetCore.csproj
dotnet add package Microsoft.Extensions.AI
dotnet add package Microsoft.Extensions.AI.OpenAI
# (other pin deps as needed)
```

### File Structure

```
examples/AIDataEngg.Web/
  AIDataEngg.Web.csproj
  Program.cs                    — Minimal API / Razor Pages host builder
  appsettings.json              — AI_ENDPOINT, AI_MODEL, etc.
  configs/                      — Copied to output, editable via web UI
    source.md
    goal.md
    signals.md
    prompt.md
  Pages/
    _Layout.cshtml              — Left nav (Signal folders + Noise + Failed), main content area
    _ViewStart.cshtml
    _ViewImports.cshtml
    Index.cshtml                — Config editor (multi-line boxes for source.md / goal.md / signals.md / prompt.md)
    Index.cshtml.cs
    Pipeline.cshtml             — "Trigger pipeline" button + live SignalR progress view
    Pipeline.cshtml.cs
    Signals.cshtml              — Left nav: list all signals with item counts. Click = filter.
    Signals.cshtml.cs
    Signal.cshtml               — Items for a specific signal (route: /Signals/{signal})
    Signal.cshtml.cs
    Item.cshtml                 — Detail view for one classified item + actions (reclassify, not-noise, retry)
    Item.cshtml.cs
    Noise.cshtml                — Items where IsNoise == true
    Noise.cshtml.cs
    Failed.cshtml               — Items where ClassificationSource == Failed
    Failed.cshtml.cs
  Hubs/
    PipelineHub.cs              — SignalR hub: StartPipeline(), broadcasts PipelineEvent
  wwwroot/
    css/
      site.css                  — Minimal custom styles (webmail-like layout)
    js/
      pipeline.js               — SignalR JS client for pipeline progress
```

### Key Design Points

**Configuration Editing** (`/` page):
- Four multi-line `<textarea>` elements, one per config file
- Load content on GET, write on POST
- Config files are read/written via `ConfigLoader` (library)
- Simple — no validation beyond what the library does at pipeline start

**Pipeline Execution** (`/Pipeline` page + SignalR hub):
- User clicks "Start Pipeline" → JS calls `connection.invoke("StartPipeline")`
- Hub method `StartPipeline()` calls `PipelineOrchestrator.RunPipelineAsync()` on a background task
- Progress is reported via `IProgress<PipelineEvent>` → hub broadcasts to all connected clients
- Pipeline view shows:
  - Current stage name with progress bar
  - Running count: items fetched, embedded, classified (auto vs LLM), failed, noise
  - A scrolling list of last N classified items (title + signal + source color-coded)

**Webmail-Style Signal Folders**:
- Left nav sidebar rendered in `_Layout.cshtml`:
  - "All Signals" → `/Signals`
  - Per signal folder → `/Signals/{signal}` (badge with count)
  - "Noise" → `/Noise` (badge with count)
  - "Failed" → `/Failed` (badge with count)
- Each folder page shows a table/list of items:
  - Title (linked to `/Item/{id}`)
  - Signal badge
  - Date
  - Source (Auto/LLM/Bootstrap/Failed)
  - Checkbox or action buttons for batch operations
- Clicking an item navigates to detail page

**Item Detail + Actions** (`/Item/{id}`):
- Shows: Title, Summary, Link, Signal, Reasoning, Attempt Count, Source, Embedding status
- Action buttons:
  - "Reclassify" → modal popup with signal dropdown + optional reason text → calls `IFeedbackService.ReclassifyAsync`
  - "Not Noise" (if IsNoise) → calls `MarkNotNoiseAsync`
  - "Retry" (if Failed) → calls `RetryFailedAsync` — re-runs LLM classification
  - "More Like This" → shows similar items in a panel below (vector search)

**SignalR Hub Contract:**

```csharp
public class PipelineHub : Hub
{
    public async Task StartPipeline();
    // Server → Client events:
    //   StageChanged { Stage: string, Message: string, Progress: double? }
    //   ItemProcessed { Title: string, Signal: string, Source: string, Index: int, Total: int }
    //   PipelineComplete { Summary: PipelineSummary }
    //   PipelineError { Message: string }
}
```

**`Program.cs` Minimal API wiring:**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddAIDataEngg(builder.Configuration);

var app = builder.Build();

app.MapRazorPages();
app.MapHub<PipelineHub>("/hub/pipeline");

app.Run();
```

### Layout Mockup (text)

```
+----------------------------------------------------------+
| 🏠 Streamix AIDataEngg   [Config] [Pipeline] [Signals]   |
+----------+-----------------------------------------------+
| 📂 All   |  Items in "AI/ML" (12)                        |
| 📂 AI/ML |                                               |
| 📂 Cloud |  ☐ [AUTO] New LLM benchmark from OpenAI       |
| 📂 Dev   |  ☐ [LLM ] AWS releases Inferentia 3           |
| 📂 Sec   |  ☐ [AUTO] .NET 10 preview features             |
| 📂 OSS   |                                               |
| 📂 Reg   |                                               |
| 📂 Gen   |  [1-10 of 12]  [<] [>]                        |
|----------|                                               |
| 🔇 Noise |                                               |
| ❌ Failed |                                               |
+----------+-----------------------------------------------+
```

---

## Changes Required in Existing Projects

### `src/Streamix.AspNetCore/`

Minor additions:
- Possibly add a `MapAIDataEnggPipeline` endpoint convention or helper. Not strictly required — the ASP.NET host can wire it manually.
- No major changes needed.

### `README.md` and `docs/`

- Update `README.md` to mention the library project as the canonical home for the pipeline logic.
- Update `examples/AIDataEngg/README.md` to note the library dependency.
- Add `README.md` to `src/Streamix.AIDataEngg/` with basic usage.

---

## Summary Counts

| Category | Files |
|---|---|
| Existing files to move (near-verbatim) | 12 |
| New library files | 6 |
| Console app remaining after slim-down | 2 + configs |
| ASP.NET host files | ~18 |
| **Total new/changed** | **~26 files** |

## Open Questions for Next Phase

1. **DbContext connection string**: Library should accept a connection string via config, defaulting to `Data Source=aidataengg.db` for SQLite. The ASP.NET host could use a different path or switch to PostgreSQL later.

2. **Config file location in ASP.NET**: `configs/` copied to output (`PreserveNewest`). For the web app, config edits write back to disk. In containerized deployment, this requires a writable volume mount. Acceptable for now.

3. **Pipeline cancellation in ASP.NET**: If the user navigates away mid-pipeline, should it continue in background or cancel? SignalR hub lifecycle means the hub method's `CancellationToken` fires when the connection drops. We could add a "cancel pipeline" button too.

4. **Multi-tenancy / auth**: Not needed for a demo. No user identity, no auth. A single "operator" uses the app.

5. **LLM endpoint configuration**: The web app needs a settings page or appsettings section for `AI_ENDPOINT`, `AI_MODEL`, `AI_API_KEY`. Could be combined with the config editor page or a separate `/Settings` page.

6. **Embedding model dimension constant**: `EmbeddingDefaults.Dimensions` is compile-time const. If the user switches embedding models, the dimension mismatch would corrupt the vector store. A runtime check on pipeline start would help.

---

## Recommendation

The extraction is straightforward — the codebase is well-structured with clear separation. The heaviest lift is:

1. **`PipelineOrchestrator`** — extracting the inline pipeline from `Program.cs` into a reusable class
2. **`IFeedbackService`** — reimagining the console feedback loop as a query/command interface
3. **Razor Pages + SignalR** — building the web UI (~10 pages + 1 hub)

The console app remains fully functional as a thin shell. The ASP.NET host becomes a proper demo vehicle for the Streamix pipeline, usable in tutorials and training without requiring terminal interaction.
