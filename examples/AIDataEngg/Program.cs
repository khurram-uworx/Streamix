using AIDataEngg.Data;
using AIDataEngg.Models;
using AIDataEngg.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using Streamix;
using Streamix.Extensions;
using System.ClientModel;

const string DefaultEndpoint = "http://localhost:11434/v1";
const string DefaultModel = "llama3.2:1b"; // "qwen3:4b";//"phi4-mini";

var endpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT") ?? DefaultEndpoint;
var modelName = Environment.GetEnvironmentVariable("AI_MODEL") ?? DefaultModel;
var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? "no-auth";

{
    await using var db = new RssDbContext();
    await db.Database.EnsureCreatedAsync();
}

var feedSources = File.ReadAllLines("configs/source.md")
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

var goal = File.ReadAllText("configs/goal.md");

var signals = File.ReadAllLines("configs/signals.md")
    .Where(l => l.TrimStart().StartsWith('-'))
    .Select(l => l.TrimStart('-', ' '))
    .ToArray();

Console.WriteLine($"Endpoint: {endpoint}");
Console.WriteLine($"Model: {modelName}");
Console.WriteLine($"Feed sources: {feedSources.Length}");
Console.WriteLine($"Signals: {signals.Length}");

for (var i = 0; i < feedSources.Length; i++)
{
    Console.WriteLine($"  {i + 1}. {feedSources[i].Name} — {feedSources[i].Url}");
}

IChatClient chatClient = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
    .GetChatClient(modelName)
    .AsIChatClient();

var signalsText = string.Join("\n", signals.Select(s => $"- {s}"));
var validSignals = signals.ToHashSet(StringComparer.OrdinalIgnoreCase);

var promptTemplate = File.ReadAllText("configs/prompt.md");
var systemPrompt = promptTemplate
    .Replace("{goalText}", goal)
    .Replace("{signalsText}", signalsText);

Console.WriteLine("Starting pipeline...");
await Flux.ScopedAsync(async scope =>
{
    var ct = scope.CancellationToken;

    // Stage 1 & 2: parallel RSS fetch + dedup save
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

    // Stage 4 & 5: sequential AI classification (Ollama small models can't handle
    //               parallel loads) with progress reporting + save results
    Console.WriteLine("[Stage 4-5] Classifying items...");

    var classifiedCount = 0;
    await EfFlux.FromStreamed(
        ctx => ctx.Set<RssItem>().Where(r => !r.Processed),
        () => new RssDbContext(),
        name: "Unprocessed")
        .Checkpoint("Classify")
        .FlatMap(async item =>
        {
            var attemptCount = 0;
            var result = await Flux
                .From(ct2 =>
                {
                    attemptCount++;
                    return ClassifyAndValidateAsync(chatClient, item, systemPrompt, validSignals, ct2);
                })
                .Retry(3)
                .OnErrorResume(ex =>
                {
                    var hallucinated = (string?)ex.Data["HallucinatedSignal"];
                    return Streamix.Single.Just(new ClassificationResult(
                        "General",
                        $"All 3 attempts failed. Last invalid signal: '{hallucinated}'",
                        true,
                        hallucinated
                    ));
                })
                .ToTask(ct);

            await using var db = new RssDbContext();
            db.RssItems.Attach(item);
            item.Processed = true;
            db.Classifications.Add(new ClassifiedRssItem
            {
                RssItemId = item.Id,
                Signal = result.Signal,
                Reasoning = result.Reasoning,
                IsNoise = result.IsNoise,
                AttemptCount = attemptCount,
                HallucinatedSignal = result.HallucinatedSignal
            });
            await db.SaveChangesAsync(ct);

            classifiedCount++;
            Console.WriteLine($"  [{classifiedCount}/{totalUnprocessed}] {item.Title} -> {result.Signal}{(result.IsNoise ? " (noise)" : "")}");
            return result;
        }, maxConcurrency: 1)
        .DrainAsync(ct);
});

Console.WriteLine("Pipeline complete.");

static async Task<ClassificationResult> ClassifyAndValidateAsync(
    IChatClient client, RssItem item, string systemPrompt,
    HashSet<string> validSignals, CancellationToken ct)
{
    var r = await RssClassifier.ClassifyAsync(client, item, systemPrompt, ct);
    if (!validSignals.Contains(r.Signal))
    {
        var ex = new InvalidOperationException($"Invalid signal '{r.Signal}' from model");
        ex.Data["HallucinatedSignal"] = r.Signal;
        throw ex;
    }
    return r;
}
