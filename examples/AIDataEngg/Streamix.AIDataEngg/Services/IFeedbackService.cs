using Streamix.AIDataEngg.Models;

namespace Streamix.AIDataEngg.Services;

public interface IFeedbackService
{
    Task<List<SignalGroup>> GetSignalsAsync(CancellationToken ct = default);
    Task<List<ClassifiedRssItem>> GetNoiseAsync(CancellationToken ct = default);
    Task<List<ClassifiedRssItem>> GetBouncedAsync(CancellationToken ct = default);
    Task<ClassifiedRssItem?> GetItemDetailsAsync(int itemId, CancellationToken ct = default);
    Task<bool> ReclassifyAsync(int itemId, string newSignal, bool isNoise, CancellationToken ct = default);
    Task<bool> DeleteItemAsync(int itemId, CancellationToken ct = default);

    Task<List<(ClassifiedRssItem Item, double Score)>> MoreLikeAsync(int classifiedId, int top = 6, CancellationToken ct = default);
    Task<Dictionary<string, int>> GetSignalCountsAsync(CancellationToken ct = default);
    Task<int> GetNoiseCountAsync(CancellationToken ct = default);
    Task<int> GetFailedCountAsync(CancellationToken ct = default);
    Task<bool> MarkNotNoiseAsync(int classifiedId, CancellationToken ct = default);
    Task<(bool Success, ClassifiedRssItem? Item)> RetryFailedAsync(int classifiedId, CancellationToken ct = default);
}

public record SignalGroup(string Signal, int Count, List<ClassifiedRssItem> Items);
