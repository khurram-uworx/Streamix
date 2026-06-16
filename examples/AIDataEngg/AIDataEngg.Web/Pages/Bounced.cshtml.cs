using Microsoft.AspNetCore.Mvc.RazorPages;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Web.Pages;

public class BouncedModel : PageModel
{
    readonly IFeedbackService feedback;

    public BouncedModel(IFeedbackService feedback)
    {
        this.feedback = feedback;
    }

    public List<ClassifiedRssItem> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        Items = await feedback.GetBouncedAsync();
    }
}
