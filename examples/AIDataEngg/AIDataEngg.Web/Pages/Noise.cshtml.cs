using Microsoft.AspNetCore.Mvc.RazorPages;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Web.Pages;

public class NoiseModel : PageModel
{
    readonly IFeedbackService feedback;

    public NoiseModel(IFeedbackService feedback)
    {
        this.feedback = feedback;
    }

    public List<ClassifiedRssItem> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        Items = await feedback.GetNoiseAsync();
    }
}
