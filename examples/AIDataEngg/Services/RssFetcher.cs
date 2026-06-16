using AIDataEngg.Models;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;

namespace AIDataEngg.Services;

public static class RssFetcher
{
    static string computeRssSha(string feedUrl, string feedName, string title, DateTimeOffset published)
    {
        var input = $"{feedUrl}|{feedName}|{title}|{published.Ticks}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    public static async IAsyncEnumerable<RssItem> FetchFeedAsync(
        string feedUrl,
        string feedName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = XmlReader.Create(feedUrl);
        var feed = SyndicationFeed.Load(reader);

        foreach (var item in feed.Items)
        {
            ct.ThrowIfCancellationRequested();

            var title = item.Title?.Text ?? "(no title)";
            var published = item.PublishDate != DateTimeOffset.MinValue
                ? item.PublishDate
                : item.LastUpdatedTime;

            yield return new RssItem
            {
                FeedUrl = feedUrl,
                FeedName = feedName,
                Title = title,
                Summary = item.Summary?.Text,
                Link = item.Links.FirstOrDefault()?.Uri?.ToString(),
                Published = published,
                ContentHash = computeRssSha(feedUrl, feedName, title, published)
            };
        }
    }
}
