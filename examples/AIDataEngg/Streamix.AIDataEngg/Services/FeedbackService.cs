using Microsoft.EntityFrameworkCore;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;
using System.Numerics.Tensors;

namespace Streamix.AIDataEngg.Services;

public class FeedbackService(RssDbContext db) : IFeedbackService
{
    public async Task<List<SignalGroup>> GetSignalsAsync(CancellationToken ct = default)
    {
        var items = await db.Classifications
            .Include(c => c.RssItem)
            .Where(c => !c.IsNoise)
            .OrderByDescending(c => c.ClassifiedAt)
            .ToListAsync(ct);

        return items
            .GroupBy(c => c.Signal, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SignalGroup(g.Key, g.Count(), g.ToList()))
            .OrderByDescending(g => g.Count)
            .ToList();
    }

    public async Task<List<ClassifiedRssItem>> GetNoiseAsync(CancellationToken ct = default)
    {
        return await db.Classifications
            .Include(c => c.RssItem)
            .Where(c => c.IsNoise)
            .OrderByDescending(c => c.ClassifiedAt)
            .ToListAsync(ct);
    }

    public async Task<List<ClassifiedRssItem>> GetBouncedAsync(CancellationToken ct = default)
    {
        return await db.Classifications
            .Include(c => c.RssItem)
            .Where(c => c.AttemptCount >= 5)
            .OrderByDescending(c => c.ClassifiedAt)
            .ToListAsync(ct);
    }

    public async Task<ClassifiedRssItem?> GetItemDetailsAsync(int itemId, CancellationToken ct = default)
    {
        return await db.Classifications
            .Include(c => c.RssItem)
            .FirstOrDefaultAsync(c => c.Id == itemId, ct);
    }

    public async Task<bool> ReclassifyAsync(int itemId, string newSignal, bool isNoise, CancellationToken ct = default)
    {
        var item = await db.Classifications.FindAsync([itemId], ct);
        if (item is null) return false;

        item.Signal = newSignal;
        item.IsNoise = isNoise;
        item.ClassifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteItemAsync(int itemId, CancellationToken ct = default)
    {
        var item = await db.Classifications.FindAsync([itemId], ct);
        if (item is null) return false;

        db.Classifications.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<(ClassifiedRssItem Item, double Score)>> MoreLikeAsync(
        int classifiedId, int top = 6, CancellationToken ct = default)
    {
        var target = await db.Classifications
            .Include(c => c.RssItem)
            .FirstOrDefaultAsync(c => c.Id == classifiedId, ct);
        if (target?.Embedding is null || target.Embedding.Length == 0)
            return [];

        var targetVec = EmbeddingSerializer.FromBytes(target.Embedding);
        var candidates = await db.Classifications
            .Include(c => c.RssItem)
            .Where(c => c.Embedding != null && c.Id != classifiedId)
            .ToListAsync(ct);

        var scores = new List<(ClassifiedRssItem Item, double Score)>();
        foreach (var c in candidates)
        {
            if (c.Embedding is null || c.Embedding.Length == 0) continue;
            var vec = EmbeddingSerializer.FromBytes(c.Embedding);
            var sim = TensorPrimitives.CosineSimilarity(targetVec.Span, vec.Span);
            scores.Add((c, sim));
        }

        return scores.OrderByDescending(x => x.Score).Take(top).ToList();
    }

    public async Task<Dictionary<string, int>> GetSignalCountsAsync(CancellationToken ct = default)
    {
        return await db.Classifications
            .Where(c => !c.IsNoise)
            .GroupBy(c => c.Signal)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, StringComparer.OrdinalIgnoreCase, ct);
    }

    public async Task<int> GetNoiseCountAsync(CancellationToken ct = default)
    {
        return await db.Classifications.CountAsync(c => c.IsNoise, ct);
    }

    public async Task<int> GetFailedCountAsync(CancellationToken ct = default)
    {
        return await db.Classifications.CountAsync(c => c.AttemptCount >= 5, ct);
    }

    public async Task<bool> MarkNotNoiseAsync(int classifiedId, CancellationToken ct = default)
    {
        var item = await db.Classifications.FindAsync([classifiedId], ct);
        if (item is null) return false;

        item.IsNoise = false;
        item.ClassifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(bool Success, ClassifiedRssItem? Item)> RetryFailedAsync(
        int classifiedId, CancellationToken ct = default)
    {
        var item = await db.Classifications
            .Include(c => c.RssItem)
            .FirstOrDefaultAsync(c => c.Id == classifiedId, ct);
        if (item?.RssItem is null)
            return (false, null);

        item.RssItem.Processed = false;
        item.AttemptCount = 0;
        item.HallucinatedSignal = null;
        await db.SaveChangesAsync(ct);

        return (true, item);
    }
}
