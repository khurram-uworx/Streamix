using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Web.Pages;

public class ItemModel(
    IFeedbackService feedback,
    ConfigLoader loader,
    IWebHostEnvironment env) : PageModel
{
    public ClassifiedRssItem? Item { get; set; }
    public List<(ClassifiedRssItem Item, double Score)> Similar { get; set; } = [];
    public string[] AllSignals { get; set; } = [];
    public string? Message { get; set; }
    public bool IsError { get; set; }

    string ConfigDir => Path.Combine(env.ContentRootPath, "App_Data", "configs");

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Item = await feedback.GetItemDetailsAsync(id);
        if (Item is null) return NotFound();

        await LoadSideDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostReclassifyAsync(int id, string newSignal, bool isNoise)
    {
        var ok = await feedback.ReclassifyAsync(id, newSignal, isNoise);
        if (!ok) return NotFound();

        Message = $"Reclassified as {newSignal}.";
        Item = await feedback.GetItemDetailsAsync(id);
        await LoadSideDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostMarkNotNoiseAsync(int id)
    {
        var ok = await feedback.MarkNotNoiseAsync(id);
        if (!ok) return NotFound();

        Message = "Marked as signal (not noise).";
        Item = await feedback.GetItemDetailsAsync(id);
        await LoadSideDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRetryAsync(int id)
    {
        var (success, _) = await feedback.RetryFailedAsync(id);
        if (!success) return NotFound();

        Message = "Item reset for re-processing. Run the pipeline to re-classify.";
        Item = await feedback.GetItemDetailsAsync(id);
        await LoadSideDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var ok = await feedback.DeleteItemAsync(id);
        if (!ok) return NotFound();

        Message = "Item deleted.";
        return RedirectToPage("Index");
    }

    async Task LoadSideDataAsync()
    {
        if (Item is not null)
        {
            Similar = await feedback.MoreLikeAsync(Item.Id);
            var config = await loader.LoadAsync(ConfigDir);
            AllSignals = config.Signals;
        }
    }
}
