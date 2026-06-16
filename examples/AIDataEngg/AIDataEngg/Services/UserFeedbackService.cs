using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.VectorData;
using Streamix;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;
using System.Runtime.CompilerServices;

namespace AIDataEngg.Services;

// Minimal interactive feedback loop for the AIDataEngg example. Reads commands
// from a TextReader (Console.In by default) via Streamix and dispatches them
// against the SQLite db + in-memory vector store. Feedback (likes / hides) is
// kept in-memory only for this version; persistence is a future concern. See
// Plan2 Task 6 for the contract.
public sealed class UserFeedbackService
{
    private readonly Func<RssDbContext> dbFactory;
    private readonly VectorStoreCollection<int, VectorIndexEntry> collection;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly HashSet<int> hidden = new();
    private readonly List<int> liked = new();

    public UserFeedbackService(
        Func<RssDbContext> dbFactory,
        VectorStoreCollection<int, VectorIndexEntry> collection,
        TextReader? input = null,
        TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(collection);

        this.dbFactory = dbFactory;
        this.collection = collection;
        this.input = input ?? Console.In;
        this.output = output ?? Console.Out;
    }

    public IReadOnlyList<int> LikedIds => liked;
    public IReadOnlyCollection<int> HiddenIds => hidden;

    public async Task RunInteractiveAsync(int recentLimit = 10, CancellationToken ct = default)
    {
        await PrintRecentAsync(recentLimit, ct).ConfigureAwait(false);
        PrintHelp();

        // Streamix idiom: turn the TextReader into an IAsyncEnumerable<string>,
        // wrap with Flux, then dispatch each command through FlatMap with
        // maxConcurrency=1 so commands run serially.
        await Flux.From(ReadCommandsAsync(ct))
            .FlatMap(async line =>
            {
                await HandleCommandAsync(line, ct).ConfigureAwait(false);
                return line;
            }, maxConcurrency: 1)
            .DrainAsync(ct).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<string> ReadCommandsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await output.WriteAsync("> ").ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);

            string? line;
            try
            {
                line = await input.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line is null) yield break; // EOF

            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (string.Equals(trimmed, "quit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            yield return trimmed;
        }
    }

    private async Task HandleCommandAsync(string command, CancellationToken ct)
    {
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : null;

        switch (verb)
        {
            case "help":
            case "?":
                PrintHelp();
                break;

            case "recent":
                await PrintRecentAsync(10, ct).ConfigureAwait(false);
                break;

            case "like" when int.TryParse(arg, out var likeId):
                liked.Add(likeId);
                await output.WriteLineAsync($"  liked id={likeId}").ConfigureAwait(false);
                break;

            case "hide" when int.TryParse(arg, out var hideId):
                hidden.Add(hideId);
                await output.WriteLineAsync($"  hidden id={hideId}").ConfigureAwait(false);
                break;

            case "morelike" when int.TryParse(arg, out var moreId):
                await PrintMoreLikeAsync(moreId, ct).ConfigureAwait(false);
                break;

            default:
                await output.WriteLineAsync($"Unknown command: {command}").ConfigureAwait(false);
                PrintHelp();
                break;
        }
    }

    private async Task PrintRecentAsync(int limit, CancellationToken ct)
    {
        await using var db = dbFactory();
        var recent = await db.Classifications
            .Include(c => c.RssItem)
            .OrderByDescending(c => c.Id)
            .Take(limit)
            .ToListAsync(ct).ConfigureAwait(false);

        await output.WriteLineAsync($"--- Recent {recent.Count} classifications ---").ConfigureAwait(false);
        foreach (var c in recent)
        {
            if (hidden.Contains(c.Id)) continue;
            var noiseTag = c.IsNoise ? " (noise)" : "";
            await output.WriteLineAsync($"  [{c.Id}] {c.Signal}{noiseTag}: {c.RssItem.Title}").ConfigureAwait(false);
        }
    }

    private async Task PrintMoreLikeAsync(int id, CancellationToken ct)
    {
        await using var db = dbFactory();
        var seed = await db.Classifications
            .Include(c => c.RssItem)
            .FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);

        if (seed is null)
        {
            await output.WriteLineAsync($"  No classification with id={id}").ConfigureAwait(false);
            return;
        }
        if (seed.Embedding is null || seed.Embedding.Length == 0)
        {
            await output.WriteLineAsync($"  No embedding stored for id={id}").ConfigureAwait(false);
            return;
        }

        var emb = EmbeddingSerializer.FromBytes(seed.Embedding);
        await output.WriteLineAsync($"--- Items similar to [{id}] {seed.RssItem.Title} ---").ConfigureAwait(false);

        var shown = 0;
        await foreach (var hit in collection.SearchAsync(emb, top: 6, cancellationToken: ct).ConfigureAwait(false))
        {
            if (hit.Record.Id == id) continue;
            if (hidden.Contains(hit.Record.Id)) continue;
            await output.WriteLineAsync(
                $"  [{hit.Record.Id}] (score={hit.Score:F3}) {hit.Record.Signal}: {hit.Record.Title}")
                .ConfigureAwait(false);
            if (++shown >= 5) break;
        }
        if (shown == 0)
        {
            await output.WriteLineAsync("  (no similar items found)").ConfigureAwait(false);
        }
    }

    private void PrintHelp()
    {
        output.WriteLine("Commands:");
        output.WriteLine("  recent             list last 10 classifications");
        output.WriteLine("  like <id>          mark a classification as liked");
        output.WriteLine("  hide <id>          suppress this classification from future displays");
        output.WriteLine("  morelike <id>      show similar classifications via vector search");
        output.WriteLine("  help / ?           print this help");
        output.WriteLine("  quit / exit        leave the feedback loop");
    }
}
