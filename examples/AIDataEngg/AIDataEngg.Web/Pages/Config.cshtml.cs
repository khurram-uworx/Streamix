using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Web.Pages;

public class ConfigModel(ConfigLoader loader, IWebHostEnvironment env) : PageModel
{
    [BindProperty]
    public string FeedSourcesText { get; set; } = string.Empty;

    [BindProperty]
    public string GoalText { get; set; } = string.Empty;

    [BindProperty]
    public string SignalsText { get; set; } = string.Empty;

    [BindProperty]
    public string PromptText { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }

    string ConfigDir => Path.Combine(env.ContentRootPath, "configs");

    public async Task OnGetAsync()
    {
        var config = await loader.LoadAsync(ConfigDir);
        FeedSourcesText = string.Join("\n", config.FeedSources.Select(s => $"{s.Name} | {s.Url}"));
        GoalText = config.Goal;
        SignalsText = string.Join("\n", config.Signals);
        PromptText = config.PromptTemplate;
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
}
