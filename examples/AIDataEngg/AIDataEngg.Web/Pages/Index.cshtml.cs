using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Streamix.AIDataEngg.Data;
using Streamix.AIDataEngg.Models;
using Streamix.AIDataEngg.Services;

namespace AIDataEngg.Web.Pages;

public class IndexModel : PageModel
{
    readonly RssDbContext db;
    readonly ConfigLoader loader;
    readonly IWebHostEnvironment env;

    public IndexModel(RssDbContext db, ConfigLoader loader, IWebHostEnvironment env)
    {
        this.db = db;
        this.loader = loader;
        this.env = env;
    }

    public int SignalCount { get; set; }
    public int NoiseCount { get; set; }
    public int BouncedCount { get; set; }
    public int TotalFeeds { get; set; }
    public List<ClassifiedRssItem> RecentItems { get; set; } = [];

    public async Task OnGetAsync()
    {
        SignalCount = await db.Classifications.CountAsync(c => !c.IsNoise);
        NoiseCount = await db.Classifications.CountAsync(c => c.IsNoise);
        BouncedCount = await db.Classifications.CountAsync(c => c.AttemptCount >= 5);

        var config = await loader.LoadAsync(Path.Combine(env.ContentRootPath, "App_Data", "configs"));
        TotalFeeds = config.FeedSources.Count;

        RecentItems = (await db.Classifications
            .Include(c => c.RssItem)
            .ToListAsync())
            .OrderByDescending(c => c.ClassifiedAt.LocalDateTime)
            .Take(10)
            .ToList();
    }
}
