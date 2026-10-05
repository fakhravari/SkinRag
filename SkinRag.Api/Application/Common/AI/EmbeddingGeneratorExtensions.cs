using Microsoft.Extensions.AI;

namespace SkinRag.Api.Application.Common.AI;

public static class EmbeddingGeneratorExtensions
{
    public static async Task<float[][]> GenerateVectorsAsync(
        this IEmbeddingGenerator<string, Embedding<float>> generator,
        IReadOnlyList<string> texts,
        string modelId,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var options = new EmbeddingGenerationOptions { ModelId = modelId };
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties["truncate"] = false;
        var generated = await generator.GenerateAsync(texts, options, cancellationToken);
        if (generated.Count != texts.Count)
        {
            throw new InvalidOperationException("The embedding provider returned an unexpected number of vectors.");
        }

        var vectors = generated.Select(embedding => embedding.Vector.ToArray()).ToArray();
        if (vectors.Any(vector => vector.Length == 0 || vector.Any(value => !float.IsFinite(value))
                                  || !vector.Any(value => value != 0)))
        {
            throw new InvalidOperationException("The embedding provider returned invalid vectors.");
        }

        return vectors;
    }
}
