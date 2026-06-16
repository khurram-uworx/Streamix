using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;

namespace AIDataEngg.Web.Pages;

public class IndexModel : PageModel
{
    readonly RssDbContext db;

    public IndexModel(RssDbContext db)
    {
        this.db = db;
    }

    public int SignalCount { get; set; }
    public int NoiseCount { get; set; }
    public int BouncedCount { get; set; }
    public int TotalFeeds { get; set; } = 4;
    public List<ClassifiedRssItem> RecentItems { get; set; } = [];

    public async Task OnGetAsync()
    {
        SignalCount = await db.Classifications.CountAsync(c => !c.IsNoise);
        NoiseCount = await db.Classifications.CountAsync(c => c.IsNoise);
        BouncedCount = await db.Classifications.CountAsync(c => c.AttemptCount >= 5);

        RecentItems = (await db.Classifications
            .Include(c => c.RssItem)
            .ToListAsync())
            .OrderByDescending(c => c.ClassifiedAt.LocalDateTime)
            .Take(10)
            .ToList();
    }
}
