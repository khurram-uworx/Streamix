using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Web.Pages;

public class ConfigModel(
    ConfigLoader loader,
    IWebHostEnvironment env,
    RssDbContext db,
    IConfiguration configuration) : PageModel
{
    // --- Configuration form fields ---
    [BindProperty]
    public string FeedSourcesText { get; set; } = string.Empty;

    [BindProperty]
    public string GoalText { get; set; } = string.Empty;

    [BindProperty]
    public string SignalsText { get; set; } = string.Empty;

    [BindProperty]
    public string PromptText { get; set; } = string.Empty;

    // --- Engine settings form fields ---
    [BindProperty]
    public string OllamaEndpoint { get; set; } = string.Empty;

    [BindProperty]
    public string EmbeddingModel { get; set; } = string.Empty;

    [BindProperty]
    public string LlmModel { get; set; } = string.Empty;

    [BindProperty]
    public string ApiKey { get; set; } = string.Empty;

    [BindProperty]
    public int EmbeddingDimension { get; set; } = 768;

    [BindProperty]
    public float MinAvgSimilarity { get; set; } = VectorClassifier.DefaultMinAvgSimilarity;

    [BindProperty]
    public float MinMargin { get; set; } = VectorClassifier.DefaultMinMargin;

    [BindProperty]
    public int BootstrapThreshold { get; set; } = VectorClassifier.DefaultBootstrapThreshold;

    [BindProperty]
    public int TopK { get; set; } = VectorClassifier.DefaultTopK;

    [BindProperty]
    public int MinNeighbors { get; set; } = VectorClassifier.DefaultMinNeighbors;

    [BindProperty]
    public int MinNeighborAgreement { get; set; } = VectorClassifier.DefaultMinNeighborAgreement;

    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }

    string ConfigDir => Path.Combine(env.ContentRootPath, "App_Data", "configs");

    string DatabasePath => Path.Combine(
        env.ContentRootPath,
        configuration["AIDataEngg:DatabasePath"] ?? "App_Data/aidataengg.db");

    public async Task OnGetAsync()
    {
        var config = await loader.LoadAsync(ConfigDir);
        FeedSourcesText = string.Join("\n", config.FeedSources.Select(s => $"{s.Name} | {s.Url}"));
        GoalText = config.Goal;
        SignalsText = string.Join("\n", config.Signals);
        PromptText = config.PromptTemplate;
        OllamaEndpoint = config.OllamaEndpoint;
        EmbeddingModel = config.EmbeddingModel;
        LlmModel = config.LlmModel;
        ApiKey = config.ApiKey;
        EmbeddingDimension = config.EmbeddingDimension;
        MinAvgSimilarity = config.MinAvgSimilarity;
        MinMargin = config.MinMargin;
        BootstrapThreshold = config.BootstrapThreshold;
        TopK = config.TopK;
        MinNeighbors = config.MinNeighbors;
        MinNeighborAgreement = config.MinNeighborAgreement;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var sources = FeedSourcesText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l =>
            {
                var parts = l.Split('|', 2);
                var name = parts[0].Trim();
                var url = parts.Length > 1 ? parts[1].Trim() : name;
                return new FeedSource(name, url);
            })
            .ToList();

        var signals = SignalsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        var config = new PipelineConfig
        {
            FeedSources = sources,
            Goal = GoalText,
            Signals = signals,
            PromptTemplate = PromptText,
        };

        try
        {
            await loader.SaveAsync(ConfigDir, config);
            Message = "Configuration saved successfully.";
        }
        catch (Exception ex)
        {
            Message = $"Error saving configuration: {ex.Message}";
            IsError = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostEngineAsync()
    {
        var config = new PipelineConfig
        {
            OllamaEndpoint = OllamaEndpoint,
            EmbeddingModel = EmbeddingModel,
            LlmModel = LlmModel,
            ApiKey = ApiKey,
            EmbeddingDimension = EmbeddingDimension,
            MinAvgSimilarity = MinAvgSimilarity,
            MinMargin = MinMargin,
            BootstrapThreshold = BootstrapThreshold,
            TopK = TopK,
            MinNeighbors = MinNeighbors,
            MinNeighborAgreement = MinNeighborAgreement,
        };

        try
        {
            await loader.SaveEngineSettingsAsync(ConfigDir, config, default);

            // Delete and recreate the SQLite database so the new dimension applies
            var dbPath = DatabasePath.Replace("Data Source=", "");
            if (System.IO.File.Exists(dbPath))
                System.IO.File.Delete(dbPath);

            await db.Database.EnsureCreatedAsync(default);

            Message = "Engine settings saved. Database recreated. Restart the app for AI endpoint/model changes to take effect.";
        }
        catch (Exception ex)
        {
            Message = $"Error saving engine settings: {ex.Message}";
            IsError = true;
        }

        return Page();
    }
}
