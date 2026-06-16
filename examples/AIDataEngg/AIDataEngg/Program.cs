using AIDataEngg;
using AIDataEngg.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using Streamix;
using Streamix.Extensions;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;
using System.ClientModel;

const string DefaultEndpoint = "http://localhost:11434/v1";
const string DefaultModel = "llama3.2:1b"; // "qwen3:4b";//"phi4-mini";
const string DefaultEmbeddingModel = EmbeddingDefaults.ModelName;
const int BootstrapThreshold = VectorClassifier.DefaultBootstrapThreshold;
const string VectorCollectionName = "rss-vectors";

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
    return await VectorStoreSmoke.RunAsync();

var endpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT") ?? DefaultEndpoint;
var modelName = Environment.GetEnvironmentVariable("AI_MODEL") ?? DefaultModel;
var embeddingModel = Environment.GetEnvironmentVariable("AI_EMBEDDING_MODEL") ?? DefaultEmbeddingModel;
var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? "no-auth";
var configDir = Path.Combine(AppContext.BaseDirectory, "configs");

await Helper.EnsureSchemaAsync();

var feedSources = File.ReadAllLines(Path.Combine(configDir, "source.md"))
    .Where(l => l.TrimStart().StartsWith('-'))
    .Select(l => l.TrimStart('-', ' '))
    .Select(l =>
    {
        var parts = l.Split('|', 2);
        var name = parts[0].Trim();
        var url = parts.Length > 1 ? parts[1].Trim() : name;
        return (Name: name, Url: url);
    })
    .ToArray();

var goal = File.ReadAllText(Path.Combine(configDir, "goal.md"));

var signals = File.ReadAllLines(Path.Combine(configDir, "signals.md"))
    .Where(l => l.TrimStart().StartsWith('-'))
    .Select(l => l.TrimStart('-', ' '))
    .ToArray();

Console.WriteLine($"Endpoint: {endpoint}");
Console.WriteLine($"Model: {modelName}");
Console.WriteLine($"Embedding model: {embeddingModel}");
Console.WriteLine($"Feed sources: {feedSources.Length}");
Console.WriteLine($"Signals: {signals.Length}");

for (var i = 0; i < feedSources.Length; i++)
    Console.WriteLine($"  {i + 1}. {feedSources[i].Name} — {feedSources[i].Url}");

Console.WriteLine($"Signals listed: {string.Join(", ", signals)}");

if (args.Contains("--config-check", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("[Config] PASS");
    return 0;
}

var openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

IChatClient chatClient = openAIClient
    .GetChatClient(modelName)
    .AsIChatClient();

IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = openAIClient
    .GetEmbeddingClient(embeddingModel)
    .AsIEmbeddingGenerator();

var embeddingService = new EmbeddingService(embeddingGenerator);

var signalsText = string.Join("\n", signals.Select(s => $"- {s}"));
var validSignals = signals.ToHashSet(StringComparer.OrdinalIgnoreCase);

var promptTemplate = File.ReadAllText(Path.Combine(configDir, "prompt.md"));
var systemPrompt = promptTemplate
    .Replace("{goalText}", goal)
    .Replace("{signalsText}", signalsText);

// Hybrid classification setup: in-memory vector store + centroid tracker, both
// repopulated from any prior classifications so subsequent runs benefit from
// cumulative learning even though the store itself is volatile.
var collection = await VectorStoreProvider.GetOrCreateCollectionAsync(VectorCollectionName);
var centroids = new CategoryCentroidTracker();

Console.WriteLine("[Stage 0] Restoring vector store + centroids from prior classifications...");
var restoredCount = await Helper.RestoreVectorStateAsync(collection, centroids);
Console.WriteLine($"  Restored {restoredCount} embeddings");

VectorClassifier.LlmFallbackDelegate llmFallback = (item, ct2) =>
    Helper.ClassifyAndValidateAsync(chatClient, item, systemPrompt, validSignals, ct2);

var classifier = new VectorClassifier(
    collection,
    llmFallback,
    validSignals,
    centroids,
    bootstrapThreshold: BootstrapThreshold);

Console.WriteLine("Starting pipeline...");
await Flux.ScopedAsync(async scope =>
{
    var ct = scope.CancellationToken;

    // Stage 1 & 2: parallel RSS fetch + dedup save (unchanged)
    Console.WriteLine("[Stage 1-2] Fetching and saving new items...");

    await Flux.From(feedSources)
        .FlatMap(source => Flux
            .From(RssFetcher.FetchFeedAsync(source.Url, source.Name, ct))
            .OnErrorResume(ex =>
            {
                Console.WriteLine($"  Error fetching \"{source.Name}\": {ex.Message}");
                return Flux.Empty<RssItem>();
            }), maxConcurrency: 4)
        .Checkpoint("Fetch")
        .FlatMap(async item =>
        {
            await using var db = new RssDbContext();
            if (await db.RssItems.AnyAsync(r => r.ContentHash == item.ContentHash, ct))
                return false;
            db.RssItems.Add(item);
            await db.SaveChangesAsync(ct);
            Console.WriteLine($"  Saved [{item.FeedName}]: {item.Title}");
            return true;
        }, maxConcurrency: 4)
        .DrainAsync(ct);

    // Stage 3: count unprocessed items for progress
    Console.WriteLine("[Stage 3] Reading unprocessed items...");

    var totalUnprocessed = await EfFlux.FromStreamed(
        ctx => ctx.Set<RssItem>().Where(r => !r.Processed),
        () => new RssDbContext(),
        name: "UnprocessedCount")
        .CountAsync(ct);

    if (totalUnprocessed == 0)
    {
        Console.WriteLine("No items to classify. Done.");
        return;
    }

    Console.WriteLine($"  Found {totalUnprocessed} items to classify.");

    // Stage 4-5: hybrid embed + classify pipeline.
    //   - Embedding stage runs at maxConcurrency=4 (Ollama embedding models are
    //     more parallel-friendly than chat models).
    //   - Classify stage runs sequentially because the LLM fallback path uses a
    //     small chat model that can't handle parallel load (existing constraint).
    long initialClassifiedCount;
    await using (var dbCount = new RssDbContext())
    {
        initialClassifiedCount = await dbCount.Classifications.LongCountAsync(ct);
    }
    Console.WriteLine($"[Stage 4-5] Hybrid classification (bootstrap threshold {BootstrapThreshold}, current count {initialClassifiedCount})...");

    var processedSoFar = 0;
    var autoCount = 0;
    var llmCount = 0;
    var embedFailures = 0;

    await EfFlux.FromStreamed(
        ctx => ctx.Set<RssItem>().Where(r => !r.Processed),
        () => new RssDbContext(),
        name: "Unprocessed")
        .Checkpoint("Embed", item => item.Title)
        .FlatMap(async item =>
        {
            try
            {
                var embedding = await embeddingService.GenerateAsync(item, ct);
                return new EmbeddedRssItem(item, embedding, EmbeddingSucceeded: true);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref embedFailures);
                Console.WriteLine($"  [EMBED-FAIL] {item.Title}: {ex.Message}");
                return new EmbeddedRssItem(item, ReadOnlyMemory<float>.Empty, EmbeddingSucceeded: false);
            }
        }, maxConcurrency: 4)
        .Checkpoint("Classify", work => work.Item.Title)
        .ForEachAsync(async work =>
        {
            var item = work.Item;
            var embedding = work.Embedding;
            var attemptCount = 0;
            var currentCount = (int)(initialClassifiedCount + processedSoFar);

            async Task<ClassificationDecision> ClassifyAttemptAsync(CancellationToken ct2)
            {
                attemptCount++;
                if (!work.EmbeddingSucceeded)
                {
                    var llmResult = await Helper.ClassifyAndValidateAsync(chatClient, item, systemPrompt, validSignals, ct2);
                    return new ClassificationDecision(
                        llmResult,
                        ClassificationSource.LlmEmbeddingFailed,
                        NeighborStats.Empty);
                }

                return await classifier.ClassifyAsync(item, embedding, currentCount, ct2);
            }

            var decision = await Flux.FromTask(ClassifyAttemptAsync)
                .RetryThenReturn(3, ex =>
                {
                    var hallucinated = ex.Data["HallucinatedSignal"] as string;
                    return new ClassificationDecision(
                        new ClassificationResult(
                            "General",
                            $"All 3 attempts failed. Last invalid signal: '{hallucinated}'",
                            IsNoise: true,
                            HallucinatedSignal: hallucinated),
                        ClassificationSource.Failed,
                        NeighborStats.Empty);
                })
                .ToTask(ct);

            await Helper.PersistAndIndexAsync(item, embedding, decision, attemptCount, collection, centroids, ct);

            processedSoFar++;
            if (decision.WasAutoLabelled) autoCount++; else llmCount++;

            var prefix = decision.WasAutoLabelled ? "[AUTO]" : "[LLM ]";
            var noiseTag = decision.Result.IsNoise ? " (noise)" : "";
            Console.WriteLine($"  {prefix} [{processedSoFar}/{totalUnprocessed}] {item.Title} -> {decision.Result.Signal}{noiseTag} ({decision.Source})");
        }, maxConcurrency: 1, cancellationToken: ct);

    // Stage 6: summary
    Console.WriteLine("[Summary]");
    Console.WriteLine($"  Auto-labelled (vector): {autoCount}");
    Console.WriteLine($"  LLM-classified:        {llmCount}");
    Console.WriteLine($"  Embedding failures:    {embedFailures}");
    Console.WriteLine($"  Total processed:       {processedSoFar}");
    if (processedSoFar > 0)
    {
        var savings = autoCount * 100.0 / processedSoFar;
        Console.WriteLine($"  Estimated LLM-call savings: {savings:F1}% ({autoCount} auto / {processedSoFar} total)");
    }
});

Console.WriteLine("Pipeline complete.");

if (!args.Contains("--no-feedback", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine();
    Console.WriteLine("Entering interactive feedback loop ('quit' to exit, '--no-feedback' to skip).");
    var feedback = new UserFeedbackService(() => new RssDbContext(), collection);
    try
    {
        await feedback.RunInteractiveAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Feedback] aborted: {ex.Message}");
    }
}

return 0;
