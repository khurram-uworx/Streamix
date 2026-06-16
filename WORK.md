# WORK.md

Running notes about constraints, deviations, and residual TODOs that are not
captured by individual PR commits. Add new entries at the top.

## 2026-06-16 — AIDataEngg hybrid classification (Plan2)

Implemented the 8-task plan in `examples/AIDataEngg-Plan2.md` as a series of
small PR-style commits on `khurram/data-engg`:

| PR  | Task | Commit  | Summary                                                              |
| --- | ---- | ------- | -------------------------------------------------------------------- |
| 1   | 1    | 3f1d970 | MEVD vector store wiring + `--smoke` test (768-dim, cosine)          |
| 2   | 7    | 0dd4c00 | Realistic configs + `--config-check` flag                            |
| 3   | 2    | a20c1fd | `EmbeddingService` + `AI_EMBEDDING_MODEL` wiring                     |
| 4   | 4    | 54882f3 | `CategoryCentroidTracker` (running-mean centroids, thread-safe)      |
| 5   | 3    | f094c1c | `VectorClassifier` with confidence-based decision logic              |
| 6   | 5    | c3f0975 | `Program.cs` rewired to hybrid pipeline + embedding BLOB on schema   |
| 7   | 6    | 75522bd | `UserFeedbackService` + `--no-feedback` flag                         |
| 8   | 8    | (this)  | End-to-end run + this WORK.md entry                                  |

### Structural shape of the change

- New services live under `examples/AIDataEngg/Services/`:
  - `VectorStoreProvider` — `Microsoft.SemanticKernel.Connectors.InMemory` factory.
  - `EmbeddingService` — `Microsoft.Extensions.AI` embedding generation.
  - `CategoryCentroidTracker` — running-average centroids per signal, thread-safe.
  - `VectorClassifier` — top-K nearest-neighbour search + confidence gates,
    falls back to an injected `LlmFallbackDelegate` for bootstrap, sparse
    neighbours, low confidence, or invalid neighbour signals.
  - `EmbeddingSerializer` — shared `byte[]` <-> `ReadOnlyMemory<float>` codec.
  - `UserFeedbackService` — interactive feedback loop (recent / like / hide /
    morelike) driven by Streamix `Flux.From(IAsyncEnumerable<string>)`.
- Schema additions: `ClassifiedRssItem.Embedding` (nullable `byte[]`).
- New CLI flags on the example: `--smoke`, `--config-check`, `--no-feedback`.
- New env var: `AI_EMBEDDING_MODEL` (defaults to `nomic-embed-text`).
- All services have dedicated unit tests under
  `tests/Streamix.Tests/AIDataEngg/` (44 new tests, 0 regressions; full suite
  654/658 — 4 pre-existing skips unrelated to this plan).

### Deviations from the plan

1. **Stages 3 and 4 were combined into one Streamix pipeline rather than two
   distinct DB-persisted stages.** The plan suggested writing embeddings to the
   DB at the end of stage 3 and reading them back in stage 4. Instead, the new
   pipeline streams `(RssItem, Embedding, Ok)` tuples in memory between an
   `Embed` checkpoint (`maxConcurrency: 4`) and a `Classify` checkpoint
   (`maxConcurrency: 1`), persisting the embedding alongside the classification
   in `PersistAndIndexAsync`. This matches the plan's "Embedding column lives
   on `ClassifiedRssItem`" decision and avoids partial-row writes when
   classification fails — if the run is interrupted before classify, the item
   stays unprocessed and gets re-embedded on the next run, which is the
   pre-existing semantics for `RssItem.Processed = false`.

2. **The vector store is rebuilt from the SQLite db on startup**, not
   persisted in its own file. The plan called for an `InMemoryVectorStore`
   which is volatile by design; on every run, `RestoreVectorStateAsync` loads
   every `ClassifiedRssItem` that has a non-null `Embedding` column and
   re-upserts it into the collection while seeding centroids. This keeps the
   SQLite db as the single source of truth.

3. **Bootstrap counter is global across runs**, not per-run. The classify
   stage reads `db.Classifications.LongCountAsync()` once at startup and
   passes that as the initial `totalClassifiedCount`, then increments locally.
   This means the bootstrap-only LLM phase activates only on a fresh database;
   subsequent runs go straight into hybrid mode.

4. **Schema mismatch handling drops the database**, not migrates. The example
   has no EF migrations; `EnsureSchemaAsync` probes for the new `Embedding`
   column on startup and on probe failure runs `EnsureDeletedAsync` followed
   by `EnsureCreatedAsync`. This is intentional: the example is for
   demonstrating Streamix patterns, not retaining data across schema versions.

5. **EF-SQLite LINQ translation gotcha** (caught during PR-8 verification):
   `c.Embedding.Length > 0` cannot be translated to SQLite SQL by EF Core 9.x
   (it gets rewritten as `.Any()` which also can't be translated for byte
   arrays). The fix is to filter only on `c.Embedding != null` server-side and
   defensively skip empty arrays client-side. `PersistAndIndexAsync` only
   writes a non-null `Embedding` when `embedding.IsEmpty` is false, so empty
   arrays should never land in the column anyway, but the client-side guard
   is kept as belt-and-suspenders.

### Residual TODOs / known gaps

- **LLM bias under tiny models:** during the live PR-8 run with
  `llama3.2:1b` the classifier returned `AI/ML` for the first few unrelated
  items (space launches, COVID studies). This is a model-quality issue, not a
  pipeline issue — `llama3.2:1b` is likely too small to choose between 7
  near-equally-likely categories and defaults to the first listed signal. Two
  follow-ups worth considering:
  - Strengthen `configs/prompt.md` to penalise lazy first-option answers
    (few-shot negative examples; explicit "do not default to AI/ML").
  - Bump the documented default model to `llama3.2:latest` (3B) or
    `qwen3:4b` once benchmarked.
- **Persistent vector store:** the InMemoryVectorStore is rebuilt every run.
  Once the example proves out, swapping to `Microsoft.SemanticKernel.Connectors.Sqlite`
  or `.Qdrant` would remove the rebuild cost and make `morelike` queries
  available without first running classification.
- **Persistent feedback:** likes / hides currently live in
  `UserFeedbackService` instance state only. A `UserFeedback` table keyed on
  `ClassifiedRssItem.Id` would let the next run weight or skip items
  accordingly.
- **No explicit unit-test for `Program.cs` orchestration.** Each component
  has its own tests; the glue is exercised end-to-end manually via
  `dotnet run` with Ollama. Adding a contract-style test that drives the
  pipeline with a `StubChatClient` + `StubEmbeddingGenerator` would harden
  regressions but was out of scope for Plan2.
- **Schema migrations:** `EnsureSchemaAsync` is destructive on column
  changes. If the example grows production-shaped persistence, switch to EF
  migrations (`dotnet ef migrations add`) before adding more columns.

### Verification status

- `dotnet restore`, `dotnet build --configuration Release`,
  `dotnet test --no-build --configuration Release` all pass from repo root.
- `--smoke` and `--config-check` short-circuits both pass.
- 44 new AIDataEngg tests pass (10 EmbeddingService + 10 CentroidTracker +
  14 VectorClassifier + 10 UserFeedback). Full Streamix.Tests suite remains
  654/658 (4 pre-existing skips, 0 failures, 0 regressions).
- Live end-to-end `dotnet run -- --no-feedback` against Ollama
  (`nomic-embed-text` 768-dim + `llama3.2:1b`) completes the full Stage 0-6
  pipeline; results appended to this entry on completion.

