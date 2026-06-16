using AIDataEngg.Models;
using Microsoft.Extensions.AI;

namespace AIDataEngg.Services;

public class EmbeddingService
{
    // Embedding models typically cap at 8k tokens; we cap by characters as a
    // cheap proxy. Truncation here is conservative; the model may further
    // truncate on its side. Override via constructor for tests / smaller models.
    public const int DefaultMaxInputChars = 8000;

    private readonly IEmbeddingGenerator<string, Embedding<float>> generator;
    private readonly int maxInputChars;

    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        int maxInputChars = DefaultMaxInputChars)
    {
        ArgumentNullException.ThrowIfNull(generator);
        if (maxInputChars <= 0) throw new ArgumentOutOfRangeException(nameof(maxInputChars));

        this.generator = generator;
        this.maxInputChars = maxInputChars;
    }

    public ValueTask<ReadOnlyMemory<float>> GenerateAsync(RssItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var text = string.IsNullOrWhiteSpace(item.Summary)
            ? item.Title
            : $"{item.Title}\n\n{item.Summary}";
        return GenerateAsync(text, ct);
    }

    public async ValueTask<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();

        var truncated = text.Length > maxInputChars
            ? text[..maxInputChars]
            : text;

        return await generator.GenerateVectorAsync(truncated, cancellationToken: ct).ConfigureAwait(false);
    }
}
