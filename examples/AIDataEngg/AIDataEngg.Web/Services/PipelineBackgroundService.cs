using AIDataEngg.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;
using System.Threading.Channels;

namespace AIDataEngg.Web.Services;

public class PipelineBackgroundService : BackgroundService
{
    readonly Channel<PipelineRequest> channel = Channel.CreateBounded<PipelineRequest>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleWriter = false,
            SingleReader = true
        });

    readonly IServiceScopeFactory scopeFactory;
    readonly IHubContext<PipelineHub> hubContext;

    int isRunning;

    public bool IsRunning => Interlocked.CompareExchange(ref isRunning, 0, 0) == 1;

    public PipelineBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHubContext<PipelineHub> hubContext)
    {
        this.scopeFactory = scopeFactory;
        this.hubContext = hubContext;
    }

    public async Task<string?> EnqueueAsync(PipelineConfig config, string connectionId, string groupName)
    {
        if (Interlocked.CompareExchange(ref isRunning, 1, 0) != 0)
            return null;

        var request = new PipelineRequest(
            RunId: Guid.NewGuid().ToString("N"),
            Config: config,
            ConnectionId: connectionId,
            GroupName: groupName);

        await channel.Writer.WriteAsync(request);
        return request.RunId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<RssDbContext>();
                var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();
                var chatClient = scope.ServiceProvider.GetRequiredService<IChatClient>();
                var centroids = scope.ServiceProvider.GetRequiredService<CategoryCentroidTracker>();

                var orchestrator = new PipelineOrchestrator(
                    db, null!, embeddingService, chatClient, centroids);

                var progress = new PipelineProgressReporter(hubContext, request);

                var result = await orchestrator.RunAsync(request.Config, progress, stoppingToken);

                await hubContext.Clients.Client(request.ConnectionId)
                    .SendAsync("PipelineComplete", result, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await hubContext.Clients.Client(request.ConnectionId)
                    .SendAsync("PipelineEvent",
                        new PipelineFailed("Pipeline", "Pipeline was cancelled."), CancellationToken.None);
            }
            catch (Exception ex)
            {
                await hubContext.Clients.Client(request.ConnectionId)
                    .SendAsync("PipelineEvent",
                        new PipelineFailed("Pipeline", ex.Message), CancellationToken.None);
            }
            finally
            {
                Interlocked.Exchange(ref isRunning, 0);
            }
        }
    }

    sealed class PipelineProgressReporter(IHubContext<PipelineHub> hubContext, PipelineRequest request)
        : IProgress<PipelineEvent>
    {
        public void Report(PipelineEvent value)
        {
            try
            {
                if (request.GroupName is not null)
                {
                    _ = hubContext.Clients.Group(request.GroupName)
                        .SendAsync("PipelineEvent", value, CancellationToken.None);
                }

                _ = hubContext.Clients.Client(request.ConnectionId)
                    .SendAsync("PipelineEvent", value, CancellationToken.None);
            }
            catch
            {
            }
        }
    }
}

public record PipelineRequest(
    string RunId,
    PipelineConfig Config,
    string ConnectionId,
    string? GroupName);
