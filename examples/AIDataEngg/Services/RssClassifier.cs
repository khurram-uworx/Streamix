using AIDataEngg.Models;
using Microsoft.Extensions.AI;

namespace AIDataEngg.Services;

public static class RssClassifier
{
    public static async Task<ClassificationResult> ClassifyAsync(
        IChatClient client,
        RssItem item,
        string systemPrompt,
        CancellationToken ct = default)
    {
        var userPrompt =
            $"""
            Title: {item.Title}
            Summary: {item.Summary ?? "(no summary)"}
            """;

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt),
        };

        var result = await client.GetResponseAsync<ClassificationResult>(messages, cancellationToken: ct);

        return result.Result;
    }
}
