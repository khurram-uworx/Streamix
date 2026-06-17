using Microsoft.AspNetCore.Mvc;
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
    public string? CurrentSignal { get; set; }
    public int TotalCount { get; set; }

    public async Task OnGetAsync([FromQuery] string? signal = null)
    {
        CurrentSignal = signal;

        if (!string.IsNullOrEmpty(signal))
        {
            var allGroups = await feedback.GetSignalsAsync();
            var group = allGroups.FirstOrDefault(g =>
                g.Signal.Equals(signal, StringComparison.OrdinalIgnoreCase));
            Groups = group is not null ? [group] : [];
            TotalCount = group?.Count ?? 0;
        }
        else
        {
            Groups = await feedback.GetSignalsAsync();
            TotalCount = Groups.Sum(g => g.Count);
        }
    }
}
