using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using OpenAI;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;
using System.ClientModel;

namespace Streamix.AIDataEngg;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIDataEnggCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configSection = configuration.GetSection("AIDataEngg");
        var config = new PipelineConfig();
        configSection.Bind(config);

        services.AddDbContext<RssDbContext>(options =>
            options.UseSqlite($"Data Source={config.DatabasePath}"));

        services.AddSingleton<ConfigLoader>();
        services.AddSingleton<CategoryCentroidTracker>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<PipelineOrchestrator>();

        services.AddTransient<EmbeddingService>();

        ConfigureAI(services, config);

        return services;
    }

    static void ConfigureAI(IServiceCollection services, PipelineConfig config)
    {
        var endpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT") ?? config.OllamaEndpoint;
        var modelName = Environment.GetEnvironmentVariable("AI_MODEL") ?? config.LlmModel;
        var embeddingModel = Environment.GetEnvironmentVariable("AI_EMBEDDING_MODEL") ?? config.EmbeddingModel;
        var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? "no-auth";

        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        services.AddChatClient(openAIClient.GetChatClient(modelName).AsIChatClient());

        services.AddEmbeddingGenerator<string, Embedding<float>>(
            _ => openAIClient.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator());
    }
}
