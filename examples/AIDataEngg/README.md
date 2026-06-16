# AIDataEngg Example

This console example demonstrates a Streamix RSS pipeline that fetches feed
items, generates embeddings, classifies items with a hybrid vector + LLM flow,
and offers a small interactive feedback loop.

The example also acts as a Streamix ergonomics exercise. It intentionally uses
`Flux.FromTask`, contextual `Checkpoint(...)`, `RetryThenReturn`, named
intermediate records, and terminal `ForEachAsync` so complex workflow
composition stays readable.

## Running

From the repository root:

```powershell
dotnet run --project examples\AIDataEngg -- --config-check
dotnet run --project examples\AIDataEngg -- --smoke
dotnet run --project examples\AIDataEngg -- --no-feedback
```

Config markdown files under `configs/` are copied to the build output and read
relative to the running application directory, for example
`bin\Release\net10.0\configs`.

## Package Pins

The example intentionally pins the AI/vector-related NuGet packages instead of
floating versions. These APIs are moving quickly, and keeping explicit versions
makes the sample easier to reproduce and review.

- `Microsoft.Extensions.AI` / `Microsoft.Extensions.AI.OpenAI` are pinned to
  `10.7.0` for the current `IChatClient` and `IEmbeddingGenerator` APIs.
- `Microsoft.SemanticKernel.Connectors.InMemory` is pinned to
  `1.74.0-preview` because the in-memory vector store connector is still a
  preview package.
- `System.Numerics.Tensors` is pinned directly to `10.0.9`. `Microsoft.Extensions.AI`
  already requires `System.Numerics.Tensors >= 10.0.9`; the direct reference is
  intentional so the SIMD-accelerated `TensorPrimitives` math used by the
  centroid tracker is visible and cannot be accidentally downgraded.

The default embedding model is `nomic-embed-text`, and its expected vector
dimension is defined in `EmbeddingDefaults.Dimensions`. If `AI_EMBEDDING_MODEL`
is changed to a model with a different dimension, update that constant too.
This example does not use EF or vector-store migrations, so local data may need
to be deleted and restored/reclassified after a dimension or schema change.
