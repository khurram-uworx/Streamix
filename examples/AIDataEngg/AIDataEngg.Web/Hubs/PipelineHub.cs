using Microsoft.AspNetCore.SignalR;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;
using AIDataEngg.Web.Services;

namespace AIDataEngg.Web.Hubs;

public class PipelineHub(
    PipelineBackgroundService pipelineService,
    IFeedbackService feedbackService) : Hub
{
    const string TriggerGroup = "PipelineObservers";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TriggerGroup);
        await base.OnConnectedAsync();
    }

    public async Task<string?> TriggerPipeline()
    {
        var configDir = Path.Combine(AppContext.BaseDirectory, "configs");
        var loader = new ConfigLoader();
        var config = await loader.LoadAsync(configDir);

        var runId = await pipelineService.EnqueueAsync(config, Context.ConnectionId, TriggerGroup);
        if (runId is null)
        {
            await Clients.Caller.SendAsync("PipelineEvent",
                new PipelineFailed("Queue", "A pipeline run is already in progress."));
        }
        return runId;
    }

    public async Task<List<SignalGroup>> GetSignals()
    {
        return await feedbackService.GetSignalsAsync();
    }

    public async Task<List<ClassifiedRssItem>> GetNoise()
    {
        return await feedbackService.GetNoiseAsync();
    }

    public async Task<List<ClassifiedRssItem>> GetBounced()
    {
        return await feedbackService.GetBouncedAsync();
    }

    public async Task<bool> Reclassify(int itemId, string newSignal, bool isNoise)
    {
        return await feedbackService.ReclassifyAsync(itemId, newSignal, isNoise);
    }

    public async Task<bool> DeleteItem(int itemId)
    {
        return await feedbackService.DeleteItemAsync(itemId);
    }

    public async Task<ClassifiedRssItem?> GetItemDetails(int itemId)
    {
        return await feedbackService.GetItemDetailsAsync(itemId);
    }

    public async Task<List<object>> MoreLike(int itemId, int top = 6)
    {
        var results = await feedbackService.MoreLikeAsync(itemId, top);
        return results.Select(r => new { r.Item.Id, r.Item.RssItem.Title, Score = Math.Round(r.Score, 4) } as object).ToList();
    }
}
