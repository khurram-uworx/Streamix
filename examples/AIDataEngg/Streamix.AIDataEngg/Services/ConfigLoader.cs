using Streamix.AIDataEngg.Models;

namespace Streamix.AIDataEngg.Services;

public class ConfigLoader
{
    public async Task<PipelineConfig> LoadAsync(string configDir, CancellationToken ct = default)
    {
        var config = new PipelineConfig { ConfigDir = configDir };

        config.FeedSources = await LoadFeedSourcesAsync(configDir, ct);
        config.Goal = await ReadAllTextIfExistsAsync(Path.Combine(configDir, "goal.md"), ct) ?? "";
        config.Signals = await LoadSignalNamesAsync(configDir, ct);
        config.PromptTemplate = await ReadAllTextIfExistsAsync(Path.Combine(configDir, "prompt.md"), ct) ?? "";
        await ApplyEngineSettingsAsync(config, configDir, ct);

        return config;
    }

    static async Task<List<FeedSource>> LoadFeedSourcesAsync(string configDir, CancellationToken ct)
    {
        var path = Path.Combine(configDir, "source.md");
        var content = await ReadAllTextIfExistsAsync(path, ct);
        if (content is null) return [];

        return content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith('-'))
            .Select(l => l.TrimStart('-', ' '))
            .Select(l =>
            {
                var parts = l.Split('|', 2);
                var name = parts[0].Trim();
                var url = parts.Length > 1 ? parts[1].Trim() : name;
                return new FeedSource(name, url);
            })
            .ToList();
    }

    static async Task<string[]> LoadSignalNamesAsync(string configDir, CancellationToken ct)
    {
        var path = Path.Combine(configDir, "signals.md");
        var content = await ReadAllTextIfExistsAsync(path, ct);
        if (content is null) return [];

        return content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith('-'))
            .Select(l => l.TrimStart('-', ' '))
            .ToArray();
    }

    public async Task SaveAsync(string configDir, PipelineConfig config, CancellationToken ct = default)
    {
        var sourceLines = config.FeedSources.Select(s => $"- {s.Name} | {s.Url}");
        await File.WriteAllTextAsync(
            Path.Combine(configDir, "source.md"),
            "# Feed Sources\n\nEach line: - Name | URL\n\n" + string.Join("\n", sourceLines) + "\n",
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(configDir, "goal.md"),
            config.Goal + "\n",
            ct);

        var signalLines = config.Signals.Select(s => $"- {s}");
        await File.WriteAllTextAsync(
            Path.Combine(configDir, "signals.md"),
            "# Signals\n\nEach RSS item should be classified into exactly one of these signals.\n\n" + string.Join("\n", signalLines) + "\n",
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(configDir, "prompt.md"),
            config.PromptTemplate + "\n",
            ct);

        await SaveEngineSettingsAsync(configDir, config, ct);
    }

    static async Task ApplyEngineSettingsAsync(PipelineConfig config, string configDir, CancellationToken ct)
    {
        var path = Path.Combine(configDir, "engine.md");
        var content = await ReadAllTextIfExistsAsync(path, ct);
        if (content is null) return;

        var settings = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith('-'))
            .Select(l => l.TrimStart('-', ' '))
            .Select(l =>
            {
                var eq = l.IndexOf('=');
                return eq > 0 ? (Key: l[..eq].Trim(), Value: l[(eq + 1)..].Trim()) : default;
            })
            .Where(t => t.Key is not null);

        foreach (var (key, value) in settings)
        {
            switch (key.ToLowerInvariant())
            {
                case "ollamaendpoint" when !string.IsNullOrEmpty(value):
                    config.OllamaEndpoint = value; break;
                case "embeddingmodel" when !string.IsNullOrEmpty(value):
                    config.EmbeddingModel = value; break;
                case "llmmodel" when !string.IsNullOrEmpty(value):
                    config.LlmModel = value; break;
                case "apikey":
                    config.ApiKey = value; break;
                case "embeddingdimension" when int.TryParse(value, out var i) && i > 0:
                    config.EmbeddingDimension = i; break;
                case "minavgsimilarity" when float.TryParse(value, out var f):
                    config.MinAvgSimilarity = f; break;
                case "minmargin" when float.TryParse(value, out var f):
                    config.MinMargin = f; break;
                case "bootstrapthreshold" when int.TryParse(value, out var i):
                    config.BootstrapThreshold = i; break;
                case "topk" when int.TryParse(value, out var i):
                    config.TopK = i; break;
                case "minneighbors" when int.TryParse(value, out var i):
                    config.MinNeighbors = i; break;
                case "minneighboragreement" when int.TryParse(value, out var i):
                    config.MinNeighborAgreement = i; break;
            }
        }
    }

    public async Task SaveEngineSettingsAsync(string configDir, PipelineConfig config, CancellationToken ct)
    {
        var lines = new[]
        {
            "- ollamaEndpoint=" + config.OllamaEndpoint,
            "- embeddingModel=" + config.EmbeddingModel,
            "- llmModel=" + config.LlmModel,
            "- apiKey=" + config.ApiKey,
            "- embeddingDimension=" + config.EmbeddingDimension,
            "- minAvgSimilarity=" + config.MinAvgSimilarity,
            "- minMargin=" + config.MinMargin,
            "- bootstrapThreshold=" + config.BootstrapThreshold,
            "- topK=" + config.TopK,
            "- minNeighbors=" + config.MinNeighbors,
            "- minNeighborAgreement=" + config.MinNeighborAgreement,
        };
        await File.WriteAllTextAsync(
            Path.Combine(configDir, "engine.md"),
            "# Engine Settings\n\nAI service configuration for the classification pipeline.\n\n" + string.Join("\n", lines) + "\n",
            ct);
    }

    static async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path, ct);
    }
}
