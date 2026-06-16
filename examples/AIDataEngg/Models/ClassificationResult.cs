namespace AIDataEngg.Models;

public record ClassificationResult(
    string Signal,
    string? Reasoning,
    bool IsNoise,
    string? HallucinatedSignal = null
);
