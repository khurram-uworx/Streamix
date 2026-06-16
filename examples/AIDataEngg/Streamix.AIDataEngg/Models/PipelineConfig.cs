namespace Streamix.AIDataEngg.Models;

public class PipelineConfig
{
    public List<FeedSource> FeedSources { get; set; } = [];
    public string Goal { get; set; } = string.Empty;
    public string[] Signals { get; set; } = [];
    public string PromptTemplate { get; set; } = string.Empty;
    public string OllamaEndpoint { get; init; } = "http://localhost:11434/v1";
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
    public string LlmModel { get; init; } = "llama3.2:1b";
    public string ConfigDir { get; init; } = "configs";
    public string DatabasePath { get; init; } = "aidataengg.db";
    public string VectorCollectionName { get; init; } = "rss-vectors";
    public int BootstrapThreshold { get; init; } = 20;
    public int MaxConcurrency { get; init; } = 4;
}

public record FeedSource(string Name, string Url);
