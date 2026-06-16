using Microsoft.AspNetCore.Mvc.RazorPages;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Web.Pages;

public class SignalsModel : PageModel
{
    readonly IFeedbackService feedback;

    public SignalsModel(IFeedbackService feedback)
    {
        this.feedback = feedback;
    }

    public List<SignalGroup> Groups { get; set; } = [];

    public async Task OnGetAsync()
    {
        Groups = await feedback.GetSignalsAsync();
    }
}
