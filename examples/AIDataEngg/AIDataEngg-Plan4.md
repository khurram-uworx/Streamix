# Goal
- Reimagine the AIDataEngg console example as a shared library + ASP.NET Razor Pages host with SignalR live view and webmail-style feedback UI

## Constraints & Preferences
- Simple to digest — GitHub reader should quickly learn what's happening
- Webmail folder metaphor (per-signal virtual folders in sidebar, noise as spam, bounced as failed)
- IProgress<PipelineEvent> for host-agnostic progress reporting
- Config editing via markdown files on disk (single-user, container-friendly)
- Pipeline runs via BackgroundService + Channel<PipelineRequest> (hub enqueues, BgService processes)
- Straight Razor Pages + vanilla CSS + SignalR JS (no Blazor, no React)
- Sleek, modern UI with animated progress bar, workflow chart, sidebar nav, card-based layout
- Console app preserves its Streamix-operator inline pipeline for tutorial value
- Classifier thresholds and other tunables should be editable from the Config page, persisted to markdown on disk
- Web and console apps should both read the same config file format for engine/classifier settings
- Embedding dimension must be dynamically configurable (different models → different vector sizes)
- Saving engine settings should delete and recreate the SQLite database (example app pragmatism)

## Progress
### Done
- Feed count stat card — IndexModel now injects ConfigLoader + IWebHostEnvironment, reads actual feed count from source.md
- Reclassify modal — shared overlay <div id="reclassify-modal"> added to _Layout.cshtml; modal JS (showReclassifyModal, closeReclassifyModal, applyReclassify, reclassifyAsNoise) in pipeline.js; modal CSS in site.css
- Signals/Noise/Bounced pages — warning button (⚠) and mail button (✉) now open the modal instead of calling reclassify() directly
- Modal dropdown populates from config — new hub method GetAllSignals() loads all signal names from signals.md (not just DB-classified ones); JS calls it to fill the <select>
- Shared partials — _ItemRow.cshtml (badge, reasoning, actions via ViewData params) and _EmptyState.cshtml (muted card) extracted to Pages/Shared/; all 4 pages (Signals, Noise, Bounced, Index) updated to use them — net −64 lines in page files
- Classifier settings config — PipelineConfig got 5 new fields (MinAvgSimilarity, MinMargin, TopK, MinNeighbors, MinNeighborAgreement); ConfigLoader reads/writes classifier.md (key=value format); classifier.md created in both App_Data/configs/ and console configs/
- Config page expanded — new Classifier Settings section with 6 labelled number inputs (step=0.01 for floats, step=1 for ints); 6 bindable properties in Config.cshtml.cs; grid CSS in site.css
- VectorClassifier defaults bumped — DefaultMinAvgSimilarity 0.84 → 0.86, DefaultMinMargin 0.08 → 0.10
- PipelineOrchestrator no longer hardcodes thresholds — passes all 6 config values to VectorClassifier constructor
- Console app updated — Program.cs reads classifier.md inline, parses key=value lines, passes all 6 values to VectorClassifier constructor; removed stale const int BootstrapThreshold
- VectorClassifierTests — assertion updated for new constant values; all 671 tests pass

### In Progress
- Rename classifier.md → engine.md — add endpoint, embeddingModel, llmModel, apiKey, embeddingDimension to cover full AI engine config
- Split Config page into two forms with separate save buttons: top form (Sources/Goal/Signals/Prompt) keeps existing behavior; engine form saves engine.md + deletes SQLite DB + recreates it via EnsureCreatedAsync()
- Make embedding dimension dynamically configurable (currently baked as compile‑time constant in [VectorStoreVector(768)] attribute on VectorIndexEntry.Embedding) — use VectorStoreRecordDefinition + InMemoryVectorStore.GetCollection(definition) overload to set dimension at runtime (confirmed viable via MS Learn docs)
- Update VectorStoreProvider to accept a dimensions parameter
- Update ServiceCollectionExtensions.ConfigureAI to read ApiKey from config (currently hardcoded "no-auth" fallback)

### Blocked
- (none)

## Key Decisions
- Execution model: BackgroundService + Channel<PipelineRequest> (not inline hub)
- Library namespace: Streamix.AIDataEngg (avoids ambiguity with console app's AIDataEngg namespace)
- PipelineEvent serialization: abstract record with [JsonDerivedType] attributes for flat {type, ...} JSON
- Orchestrator uses simple foreach loops internally (not Streamix operators) to stay host-agnostic
- MoreLikeAsync computes cosine similarity via TensorPrimitives.CosineSimilarity directly against DB embeddings (no vector store dependency)
- Per-signal folders rendered by JS from updateCounts() rather than server-side in layout — avoids DB calls on every page load
- Config files in App_Data/configs/ for web project (writable by ASP.NET); console app keeps its own configs/ at build output
- Classifier tightening: prompt guardrails + threshold bumps, not per-category logic
- Reclassify modal uses config‑based signals via GetAllSignals() hub method rather than DB‑only signal list — dropdown shows all possible categories even if unclassified
- VectorStoreRecordDefinition approach for dynamic dimensions — confirmed via MS Learn docs that InMemoryVectorStore.GetCollection(name, definition) overload accepts VectorStoreRecordVectorProperty(dimensions: N); no need for compile‑time [VectorStoreVector] attribute
- Engine config changes require app restart because IChatClient and IEmbeddingGenerator are registered at startup — acceptable for an example app (save → restart → run pipeline → see results)

## Next Steps
- Implement engine.md rename: update ConfigLoader (load/save engine fields), PipelineConfig (ApiKey, EmbeddingDimension; OllamaEndpoint/EmbeddingModel/LlmModel from init to set), create engine.md in both config dirs, delete old classifier.md, update console Program.cs
- Split Config.cshtml into two <form> sections with separate asp‑page‑handler="Engine" and inline validator messages
- Add OnPostEngineAsync() handler that saves engine config, resolves DB path, deletes SQLite file, calls db.Database.EnsureCreatedAsync(), signals user to restart
- Remove [VectorStoreVector] attribute from VectorIndexEntry.Embedding; change EmbeddingDefaults.Dimensions from const to static int
- Update VectorStoreProvider.GetOrCreateCollectionAsync to accept dimensions param and build VectorStoreRecordDefinition with VectorStoreRecordVectorProperty
- Thread config.EmbeddingDimension through PipelineOrchestrator and console Program.cs to VectorStoreProvider
- Update ServiceCollectionExtensions.ConfigureAI to read ApiKey from config.ApiKey before falling back to "no-auth"

### Critical Context
- Package pins: MEAI 10.7.0, SK.Connectors.InMemory 1.74.0-preview, System.Numerics.Tensors 10.9.0, EF.Sqlite 10.9.0
- Config format: markdown files in App_Data/configs/ (web) or configs/ (console) — source.md, goal.md, signals.md, prompt.md, classifier.md (soon engine.md)
- Console namespace duality: using AIDataEngg (Helper, VectorStoreSmoke, UserFeedbackService) + using Streamix.AIDataEngg.* (library types)
- Web DI: AddAIDataEnggCore(builder.Configuration) registers all library services (RssDbContext, ConfigLoader, CategoryCentroidTracker, IFeedbackService, EmbeddingService, PipelineOrchestrator, IChatClient, IEmbeddingGenerator)
- Vector store: In-memory (InMemoryVectorStore), restored from DB embeddings each pipeline run; dimension attribute on VectorIndexEntry is compile‑time constant — will be replaced with runtime VectorStoreRecordDefinition
- Pipeline dedup: SHA256 hash of {feedUrl}|{feedName}|{title}|{published.Ticks}
- DB path: App_Data/aidataengg.db (auto-created on startup by EnsureDbAsync)
- Port: http://localhost:4792
- SignalR connection established on every page via <script>connectHub();</script> in _Layout.cshtml
- 671 tests pass, 4 skipped, 0 build errors

### Relevant Files
- examples/AIDataEngg/Streamix.AIDataEngg/ — shared library (Models, Services, Data, ServiceCollectionExtensions)
- examples/AIDataEngg/AIDataEngg/ — reduced console app (Program.cs reads classifier.md inline, passes all 6 threshold values)
- examples/AIDataEngg/AIDataEngg.Web/ — web host (Program.cs, PipelineBackgroundService.cs, PipelineHub.cs, 6 Razor Pages, CSS, JS)
- examples/AIDataEngg/AIDataEngg.Web/App_Data/configs/classifier.md — classifier threshold settings (to be renamed engine.md)
- examples/AIDataEngg/AIDataEngg/configs/classifier.md — console copy of same file
- examples/AIDataEngg/Streamix.AIDataEngg/Models/PipelineConfig.cs — 5 new classifier fields + BootstrapThreshold, MinAvgSimilarity, MinMargin, TopK, MinNeighbors, MinNeighborAgreement (all set)
- examples/AIDataEngg/Streamix.AIDataEngg/Services/ConfigLoader.cs — loads/saves classifier.md via ApplyClassifierSettingsAsync / SaveClassifierSettingsAsync
- examples/AIDataEngg/Streamix.AIDataEngg/Models/VectorIndexEntry.cs — [VectorStoreVector(Dimensions: 768)] on Embedding (to be replaced with runtime definition)
- examples/AIDataEngg/Streamix.AIDataEngg/Services/VectorStoreProvider.cs — GetOrCreateCollectionAsync(name) (to accept dimensions param + VectorStoreRecordDefinition)
- examples/AIDataEngg/Streamix.AIDataEngg/Services/PipelineOrchestrator.cs — passes config values to VectorClassifier, will pass EmbeddingDimension to store provider
- examples/AIDataEngg/AIDataEngg.Web/Pages/Shared/_ItemRow.cshtml — shared row partial (badge, reasoning, actions)
- examples/AIDataEngg/AIDataEngg.Web/Pages/Shared/_EmptyState.cshtml — shared empty-state card partial
- examples/AIDataEngg/AIDataEngg.Web/Pages/Config.cshtml — Classifier Settings section added (soon to be split into two forms)
- examples/AIDataEngg/AIDataEngg.Web/Pages/Config.cshtml.cs — 6 bindable properties for classifier thresholds
- examples/AIDataEngg/AIDataEngg.Web/Hubs/PipelineHub.cs — GetAllSignals() method loads from config file
- examples/AIDataEngg/AIDataEngg.Web/wwwroot/js/pipeline.js — modal functions (showReclassifyModal, closeReclassifyModal, applyReclassify, reclassifyAsNoise)
- tests/Streamix.Tests/AIDataEngg/VectorClassifierTests.cs — updated assertions for 0.86/0.10 defaults
