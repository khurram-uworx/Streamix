using Streamix.AIDataEngg.Models;

namespace Streamix.AIDataEngg.Services;

public static class SignalCoalescer
{
    public static Dictionary<string, List<ClassifiedRssItem>> Coalesce(
        IEnumerable<ClassifiedRssItem> items)
    {
        return items
            .Where(i => !i.IsNoise)
            .GroupBy(i => i.Signal, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }
}
