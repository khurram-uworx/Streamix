using System.Runtime.InteropServices;

namespace AIDataEngg.Services;

// Embeddings are persisted as little-endian raw float bytes
// (length = dimensions * sizeof(float)). Used by Program.cs to round-trip
// embeddings between the SQLite ClassifiedRssItem.Embedding column and the
// in-memory vector store, and by UserFeedbackService to load embeddings on
// demand for "morelike" queries.
public static class EmbeddingSerializer
{
    public static byte[] ToBytes(ReadOnlyMemory<float> embedding)
    {
        if (embedding.IsEmpty) return Array.Empty<byte>();
        return MemoryMarshal.AsBytes(embedding.Span).ToArray();
    }

    public static ReadOnlyMemory<float> FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) return ReadOnlyMemory<float>.Empty;
        if (bytes.Length % sizeof(float) != 0)
            throw new ArgumentException("Embedding byte length must be a multiple of 4.", nameof(bytes));
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
