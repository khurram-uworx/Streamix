# AIDataEngg Plan 2 — Hybrid Vector + LLM Classification

> **IMPORTANT** This was the plan used for the execution by coding agents, things have changed since then, don't consider this as a source of truth, it's a historical artifact. The code as it is in the repo is the source of truth.

## Purpose

This document breaks the hybrid AI classification enhancement for the `examples/AIDataEngg` project into concrete, assignable tasks. It extends the existing RSS-fetch-and-classify pipeline with `Microsoft.Extensions.VectorData` embeddings, vector-search-based auto-classification, confidence-based decision logic, category centroids, and user feedback scaffolding.

The design follows the hybrid strategy described in `examples/Hybrid-AI.md`.

---

## How To Use

- Tasks are ordered by dependency; earlier tasks should be completed before later ones.
- Parallel-safe tasks are explicitly marked in Coordination Notes.
- Each task includes acceptance criteria and likely files so that coding agents can own them end-to-end.

## Important: Use microsoft-learn MCP for API Verification

`Microsoft.Extensions.VectorData` and `Microsoft.Extensions.AI` are rapidly evolving areas — new packages, API changes, and provider connectors ship frequently. **Before writing any code that references these libraries**, coding agents MUST:

1. Use the `microsoft-learn_microsoft_docs_search` tool to search for the latest API surface (e.g., `"Microsoft.Extensions.VectorData VectorStoreCollection SearchAsync"`, `"IEmbeddingGenerator GenerateVectorAsync .NET 10"`).
2. Use `microsoft-learn_microsoft_docs_fetch` to read the full documentation page for confirmation.
3. Verify NuGet package IDs and versions rather than assuming them (e.g., `dotnet package search Microsoft.SemanticKernel.Connectors.InMemory --take 1`).

Key queries to run per task:
- **Task 1**: `"Microsoft.Extensions.VectorData.Abstractions NuGet package .NET 10"`, `"VectorStoreRecord attribute MEVD"`, `"InMemoryVectorStore MEVD connector"`.
- **Task 2**: `"IEmbeddingGenerator GenerateVectorAsync extension method"`, `"OpenAIClient GetEmbeddingGenerator .NET"`.
- **Task 3**: `"VectorStoreCollection SearchAsync MEVD"`, `"VectorSearchResult MEVD"`.
- **Task 5**: `"EfFlux Streamix Extensions"`, `"Flux FlatMap Streamix"`.

This ensures generated code targets the actual shipping API surface, not stale assumptions.

---

## Suggested Execution Order

1. **Task 1**: Add NuGet dependencies and data model for vector storage
2. **Task 2**: Implement embedding generation service
3. **Task 3**: Implement vector classifier with confidence-based decision logic
4. **Task 4**: Implement category centroid tracking
5. **Task 5**: Rewire `Program.cs` pipeline to use hybrid classification
6. **Task 6**: Add user feedback scaffolding
7. **Task 7**: Populate sample config files for testing
8. **Task 8**: End-to-end verification and documentation

---

## Coordination Notes

- **Task 1** is a prerequisite for all subsequent tasks.
- **Tasks 2, 3, 4** can be implemented in parallel once Task 1 is done — they depend only on the data model and package references.
- **Task 5** depends on Tasks 2, 3, 4.
- **Task 6** depends on Task 5.
- **Task 7** can run in parallel with anything; it only touches config files.
- **Task 8** is the final verification gate.
- Shared files that may create merge conflicts: `AIDataEngg.csproj`, `Program.cs`, `Models/ClassifiedRssItem.cs`.

---

## Task 1: Add NuGet Dependencies and Vector Data Model

### Priority

High

### Goal

Add the `Microsoft.Extensions.VectorData` packages and define the data model types needed for storing and retrieving embeddings in a vector store.

### Why this exists

The current pipeline has no embedding or vector storage capability. MEVD (`Microsoft.Extensions.VectorData`) is Microsoft's official abstraction for vector stores — adding it lets us swap providers (InMemory, SQLite, Azure AI Search, etc.) without changing business logic.

### Scope

- Add NuGet packages to `AIDataEngg.csproj`:
  - `Microsoft.Extensions.VectorData.Abstractions` (v10.7.0)
  - `Microsoft.SemanticKernel.Connectors.InMemory` (for prototyping — per MS docs, despite the name it has nothing to do with Semantic Kernel)
- Create `Models/VectorIndexEntry.cs` — a record annotated with `[VectorStoreRecord]` attributes that maps to MEVD collections:
  - Key (int or string)
  - `Signal` (string — the classification label)
  - `Embedding` (ReadOnlyMemory\<float\> — the vector)
  - `RssItemId` (int — FK to the item)
  - `Title` / `Summary` text fields (optional, for future hybrid search)
- Create `Services/VectorStoreProvider.cs` — a thin factory that creates and returns an `InMemoryVectorStore` instance, with a comment showing how to swap to other providers.

### Constraints

- Must not break the existing pipeline — the old code path must still compile until Task 5 rewires `Program.cs`.
- The `VectorIndexEntry` model must work with MEVD's attribute-based schema.
- Keep the InMemory provider for now; document swap points for SQLite/Azure AI Search.

### Acceptance criteria

- `dotnet build` succeeds with the new packages.
- `VectorIndexEntry` can be upserted into an `InMemoryVectorStore` collection and retrieved via `GetAsync`.
- A unit-level smoke test in `tests/Streamix.Tests` (or a standalone console snippet) demonstrates creating a collection, upserting a record, and performing a vector search.

### Files likely involved

- `examples/AIDataEngg/AIDataEngg.csproj`
- `examples/AIDataEngg/Models/VectorIndexEntry.cs` (new)
- `examples/AIDataEngg/Services/VectorStoreProvider.cs` (new)

---

## Task 2: Implement Embedding Generation Service

### Priority

High

### Goal

Create an `EmbeddingService` that generates `ReadOnlyMemory<float>` embedding vectors from RSS item text using `IEmbeddingGenerator<string, Embedding<float>>`.

### Why this exists

The hybrid classification strategy requires an embedding for every RSS item. This service wraps the `Microsoft.Extensions.AI` `IEmbeddingGenerator` interface (already referenced in the project) and exposes a clean API that the pipeline can call.

### Scope

- Create `Services/EmbeddingService.cs`:
  - Constructor takes `IEmbeddingGenerator<string, Embedding<float>>` (or a delegate/func to create one).
  - `GenerateAsync(RssItem item, CancellationToken ct)` → `ValueTask<ReadOnlyMemory<float>>`
    - Concatenates `item.Title` and `item.Summary` as the input text.
    - Calls `generator.GenerateVectorAsync(text)`.
  - `GenerateAsync(string text, CancellationToken ct)` → `ValueTask<ReadOnlyMemory<float>>`
- Use the existing Ollama endpoint but with a separate `AI_EMBEDDING_MODEL` env variable (default: `nomic-embed-text` or `all-minilm` if available).
- Add the embedding model variable to `Program.cs` (alongside the existing `modelName`):
  ```csharp
  var embeddingModel = Environment.GetEnvironmentVariable("AI_EMBEDDING_MODEL") ?? "nomic-embed-text";
  ```
- Register the embedding generator in `Program.cs`:
  ```csharp
  IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator =
      new OpenAIClient(...)
          .GetEmbeddingGenerator(embeddingModel)
          .AsIEmbeddingGenerator();
  ```

### Constraints

- Must handle cancellation gracefully and not swallow cancellation tokens.
- Input text should be truncated to a reasonable max length to avoid token limits (e.g., 8000 chars).

### Acceptance criteria

- `EmbeddingService.GenerateAsync` returns a non-empty `ReadOnlyMemory<float>` for a sample `RssItem`.
- The service respects cancellation.
- `dotnet build` succeeds.

### Files likely involved

- `examples/AIDataEngg/Services/EmbeddingService.cs` (new)
- `examples/AIDataEngg/Program.cs` (add embedding model variable + generator registration)

---

## Task 3: Implement Vector Classifier with Confidence-Based Decision Logic

### Priority

High

### Goal

Implement the hybrid classification core: given an item's embedding, search the vector store for similar items, compute confidence, and decide whether to auto-label or fall back to the LLM.

### Why this exists

This is the central idea of the Hybrid-AI.md proposal — avoid LLM calls for high-confidence items by using vector similarity. The classifier implements the three confidence signals: similarity strength, neighbor agreement, and margin between top categories.

### Scope

- Create `Services/VectorClassifier.cs`:
  ```csharp
  public class VectorClassifier
  {
      public VectorClassifier(
          VectorStoreCollection<int, VectorIndexEntry> collection,
          IChatClient chatClient,
          string systemPrompt,
          HashSet<string> validSignals,
          int bootstrapThreshold = 20);

      public async Task<ClassificationResult> ClassifyAsync(
          RssItem item,
          ReadOnlyMemory<float> embedding,
          int totalClassifiedCount,
          CancellationToken ct);
  }
  ```
- Bootstrap phase: if `totalClassifiedCount < bootstrapThreshold`, always call the LLM.
- Post-bootstrap phase:
  1. Search vector store for top 10 nearest neighbors using `SearchAsync`.
  2. If fewer than `minNeighbors` (e.g., 5) exist in total, fall back to LLM.
  3. Compute confidence signals:
     - **Average similarity** of the top 5 matching neighbors.
     - **Neighbor agreement** — count how many of the top 5 share the same signal.
     - **Margin** — difference between top category's avg similarity and second category's avg similarity.
  4. Auto-classify if:
     ```
     avgSimilarity >= 0.84
     AND neighborAgreement >= 5
     AND margin >= 0.08
     ```
  5. If auto-classify: return `ClassificationResult` with the majority signal.
  6. If not: call LLM via the existing `RssClassifier.ClassifyAsync`, then upsert the result into the vector store.
- Expose the confidence threshold constants as `public static readonly` fields for testing/documentation.

### Constraints

- Must parameterize `SearchAsync` top-K and the confidence thresholds (even if hardcoded for now, they should be easy to find and tweak).
- Must handle the case where the vector store is empty (no neighbors → always LLM).
- The LLM fallback path should reuse the existing `RssClassifier.ClassifyAsync` and retry logic.

### Acceptance criteria

- When vector store has no entries, `ClassifyAsync` returns an LLM result.
- When vector store has entries with high similarity to the query, `ClassifyAsync` returns the auto-labeled result without calling the LLM.
- When similarity is low, `ClassifyAsync` falls back to the LLM.
- All three confidence signals are computed and logged.
- `dotnet build` succeeds.

### Files likely involved

- `examples/AIDataEngg/Services/VectorClassifier.cs` (new)
- `examples/AIDataEngg/Services/RssClassifier.cs` (minor: ensure its API is compatible)

---

## Task 4: Implement Category Centroid Tracking

### Priority

Medium

### Goal

Maintain running average embeddings per signal category so the classifier can also compare items against category centroids in addition to item-to-item similarity.

### Why this exists

Hybrid-AI.md describes centroids as a complementary signal: a centroid is the average embedding of all items within a category. If both nearest-neighbor similarity and centroid similarity agree, confidence is higher.

### Scope

- Create `Services/CategoryCentroidTracker.cs`:
  ```csharp
  public class CategoryCentroidTracker
  {
      // key = signal name, value = running average vector + count
      public void AddOrUpdate(string signal, ReadOnlyMemory<float> embedding);
      public float GetCentroidSimilarity(string signal, ReadOnlyMemory<float> embedding);
      public (string Signal, float Score)? GetBestCentroidMatch(ReadOnlyMemory<float> embedding, float minSimilarity = 0.7f);
  }
  ```
- The centroid is a simple running average: `newAvg = (oldAvg * count + newVector) / (count + 1)`.
- Similarity is cosine similarity between the item's embedding and the centroid.
- Integrate with `VectorClassifier`: when auto-labeling, also check centroid agreement as an additional confidence booster (optional signal, not a hard gate).
- Thread-safety: centroids are updated after each classification; use a lock or `ConcurrentDictionary`.

### Constraints

- Centroid data is in-memory only for this version (persistence is a future concern).
- Must handle normalization of embeddings before computing centroids (assume they come pre-normalized from the embedding model, but normalize on insert just in case).

### Acceptance criteria

- After adding several items with the same signal, `GetCentroidSimilarity` returns a meaningful score.
- `GetBestCentroidMatch` returns the correct signal for an embedding similar to the centroid.
- The tracker is thread-safe for concurrent updates.
- `dotnet build` succeeds.

### Files likely involved

- `examples/AIDataEngg/Services/CategoryCentroidTracker.cs` (new)

---

## Task 5: Rewire Program.cs Pipeline to Use Hybrid Classification

### Priority

High

### Goal

Restructure `Program.cs` to incorporate the full hybrid pipeline: embedding generation → vector search classification with confidence check → LLM fallback → save results + update centroids.

### Why this exists

The current pipeline (Stages 1-5) does parallel RSS fetch/dedup, then sequential LLM classification. We need to insert embedding generation before classification and add the vector-based auto-classification branch.

### Scope

Restructure `Program.cs` into these stages:

1. **Stage 1-2**: Parallel RSS fetch + dedup (unchanged from current code).
2. **Stage 3**: Read unprocessed items, generate embeddings in parallel (`FlatMap` with `maxConcurrency: 4`), save embeddings alongside items in DB.
3. **Stage 4**: Bootstrap mode — first `bootstrapThreshold` items always go through LLM. Then switch to hybrid:
   - For each item: run `VectorClassifier.ClassifyAsync`
   - On auto-label: save classification + upsert embedding to vector store + update centroid
   - On LLM fallback: call LLM, save classification + upsert embedding + update centroid
4. **Stage 5**: Summary/statistics (how many auto-labeled vs LLM-classified, cost savings estimate).

Keep the existing `Flux.ScopedAsync` structure, `Checkpoint` operators, retry logic, and error handling.

Key variables at the top of `Program.cs`:
```csharp
var bootstrapThreshold = 20;
var embeddingModel = Environment.GetEnvironmentVariable("AI_EMBEDDING_MODEL") ?? "nomic-embed-text";
```

### Constraints

- Must not break the existing working pipeline; the old flow can serve as a comparison baseline.
- `FlatMap` for embedding generation must have bounded concurrency.
- The vector store should be populated as items are classified so subsequent items benefit.
- Log auto-label vs LLM decisions with item titles so the user can verify correctness.

### Acceptance criteria

- `dotnet run` executes the full hybrid pipeline without errors.
- Console output shows per-item decisions: `[AUTO]` vs `[LLM]` prefix.
- After processing, the vector store contains all classified items' embeddings.
- After processing, centroids are populated.
- Summary line shows auto-labeled count vs LLM-classified count.

### Files likely involved

- `examples/AIDataEngg/Program.cs`
- `examples/AIDataEngg/Models/ClassifiedRssItem.cs` (add embedding BLOB column)
- `examples/AIDataEngg/Data/RssDbContext.cs` (add `VectorIndexEntry` DbSet if persisting via EF)

---

## Task 6: Add User Feedback Scaffolding

### Priority

Medium

### Goal

Add a simple feedback mechanism where the console app can accept user feedback signals (like/hide/more-like-this) that adjust the vector store and centroids, demonstrating the learning loop described in Hybrid-AI.md.

### Why this exists

User feedback is a core part of the Hybrid-AI vision — actions like "hide this" or "more like this" should propagate back into the classification model. Even a simple console-based demo proves the concept.

### Scope

- Create `Services/UserFeedbackService.cs`:
  - After the pipeline runs, enter an interactive feedback loop.
  - Show recently classified items (last 5-10).
  - Accept commands: `like <id>`, `hide <id>`, `morelike <id>`.
  - On `morelike`: search vector store for similar items using the feedback item's embedding, display them.
  - On `hide`: tag the item as suppressed (future classifications should deprioritize it).
  - Store feedback in a new `UserFeedback` table (or a simple in-memory list for this version).
- The feedback loop should demonstrate how Streamix can process interactive input streams:
  ```csharp
  await Flux.From(Console.In.ReadLineAsync)
      .FlatMap(...)
      .ForEachAsync(...);
  ```

### Constraints

- The feedback loop is optional (skippable with a `--no-feedback` flag or env var).
- Feedback data is in-memory only for this version.

### Acceptance criteria

- After the pipeline completes, the user can see a list of recent classifications and type feedback commands.
- `morelike <id>` returns and displays similar items from the vector store.
- `hide <id>` silently suppresses the item from future display.
- `dotnet build` succeeds.

### Files likely involved

- `examples/AIDataEngg/Services/UserFeedbackService.cs` (new)
- `examples/AIDataEngg/Program.cs` (invoke feedback loop after pipeline)

---

## Task 7: Populate Sample Config Files for Testing

### Priority

Medium

### Goal

Fill in the currently blank config files (`goal.md`, `signals.md`, `source.md`) with realistic sample data so the pipeline can be run end-to-end without manual editing.

### Why this exists

The current configs have placeholder values (empty goal, generic "Signal 1"/"Signal 2", example RSS feed URLs that won't resolve). Realistic configs make the example runnable and demonstrate the intended use case.

### Scope

- `configs/goal.md` — describe a real business scenario (e.g., "A tech intelligence platform monitoring AI, cloud infrastructure, and developer tooling announcements").
- `configs/signals.md` — define 5-7 specific signals:
  - AI/ML
  - Cloud Infrastructure
  - Developer Tools
  - Security
  - Open Source
  - Industry Regulation
  - General
- `configs/source.md` — add 3-4 real, publicly accessible RSS feeds that match the signals (e.g., Microsoft DevBlogs, GitHub Blog, The Verge tech news, Ars Technica).
- `configs/prompt.md` — the existing prompt template is fine; ensure it references the goal and signals correctly.

### Constraints

- RSS feeds must be publicly accessible without authentication.
- Signals should be distinct enough that the vector classifier can meaningfully distinguish them.
- Keep the format compatible with the existing parsing logic in `Program.cs`.

### Acceptance criteria

- `dotnet run` parses all config files without errors.
- RSS feeds (if available) return items that can be fetched and classified.
- The signals are realistic and produce interesting classification variety.

### Files likely involved

- `examples/AIDataEngg/configs/goal.md`
- `examples/AIDataEngg/configs/signals.md`
- `examples/AIDataEngg/configs/source.md`

---

## Task 8: End-to-End Verification and Documentation

### Priority

High (gating task)

### Goal

Verify the entire hybrid pipeline works end-to-end, and write a brief `WORK.md` entry documenting the implementation decisions, known limitations, and future directions.

### Why this exists

Without verification, we can't be sure the pieces integrate correctly. Documentation ensures the next agent or contributor understands the design choices.

### Scope

- Run `dotnet build --configuration Release` and fix any compilation errors.
- Run `dotnet test --configuration Release` to ensure no regressions in the core Streamix library.
- Run the example with `dotnet run --project examples/AIDataEngg` and verify:
  - RSS feeds are fetched (or gracefully handled if offline).
  - Embeddings are generated.
  - Bootstrap phase uses LLM for initial items.
  - Post-bootstrap phase shows auto-label decisions.
  - Centroids are computed.
  - Console output is informative and debuggable.
- Document in `WORK.md`:
  - Provider used: InMemory VectorStore + Ollama embedding model.
  - Confidence thresholds chosen and rationale.
  - Bootstrap threshold.
  - Known limitations (in-memory centroids, no persistence, single-user).
  - Future directions (web UI, persistent vector store, user feedback DB, multi-user).

### Acceptance criteria

- `dotnet build --configuration Release` succeeds.
- `dotnet test --configuration Release` passes all tests.
- The example runs and produces meaningful output (auto vs LLM decisions).
- `WORK.md` is updated with implementation notes.

### Files likely involved

- `docs/WORK.md` (create or update)
- All files from Tasks 1-7
