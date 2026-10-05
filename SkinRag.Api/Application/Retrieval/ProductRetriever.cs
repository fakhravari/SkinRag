using System.Diagnostics;
using Microsoft.Extensions.AI;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Common.AI;
using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Application.Contracts.Catalog;

namespace SkinRag.Api.Application.Retrieval;

public sealed class ProductRetriever(
    IProductRepository repository,
    IKnowledgeIndex index,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IConfiguration configuration) : IProductRetriever
{
    public async Task<RetrievalResult> RetrieveAsync(SearchPlan plan, CancellationToken ct)
    {
        var sqlTimer = Stopwatch.StartNew();
        var eligible = await repository.EligibleIdsAsync(plan, ct);
        sqlTimer.Stop();
        var sqlFilterMs = sqlTimer.Elapsed.TotalMilliseconds;
        if (eligible.Length == 0)
        {
            return new RetrievalResult([], 0, index.Snapshot().UpdatedAtUtc, "sql-filters", sqlFilterMs);
        }

        if (plan.ProductIds.Length > 0)
        {
            var directLoadTimer = Stopwatch.StartNew();
            var direct = await repository.LoadAsync(eligible, plan, ct);
            directLoadTimer.Stop();
            return new RetrievalResult(
                direct.OrderBy(p => Array.IndexOf(plan.ProductIds, p.Id)).Select(p => new ProductMatch(p, 1, 1))
                    .ToArray(),
                eligible.Length,
                null,
                "sql-product-reference",
                sqlFilterMs,
                ProductLoadMs: directLoadTimer.Elapsed.TotalMilliseconds);
        }

        var snapshot = index.Snapshot();
        if (!snapshot.IsReady)
        {
            throw new IndexNotReadyException("پایگاه دانش هنوز آماده نیست. کمی بعد دوباره تلاش کنید.");
        }

        var eligibleSet = eligible.ToHashSet();
        var candidates = snapshot.Documents.Where(d => eligibleSet.Contains(d.ProductId)).ToArray();
        if (candidates.Length == 0)
        {
            return new RetrievalResult([], eligible.Length, snapshot.UpdatedAtUtc, "hybrid-vector-lexical");
        }

        var embeddingTimer = Stopwatch.StartNew();
        var embeddingText = (configuration["Rag:QueryPrefix"] ?? "") + plan.Query;
        var vector = (await embeddingGenerator.GenerateVectorsAsync([embeddingText],
            configuration["Ollama:EmbeddingModel"] ?? "bge-m3", ct))[0];
        embeddingTimer.Stop();
        if (candidates.Any(d => d.Embedding.Length != vector.Length))
        {
            throw new IndexNotReadyException("ابعاد بردار مدل تغییر کرده است؛ بازسازی کامل دانش لازم است.");
        }

        var tokens = PersianText.SearchTokens(plan.Query);
        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        var minimum = configuration.GetValue("Rag:MinimumSimilarity", .20);
        var topK = Math.Clamp(configuration.GetValue("Rag:TopK", 5), 1, 10);
        var ranked = candidates.Select(d => new
            {
                Document = d,
                Similarity = Cosine(vector, d.Embedding, norm, d.VectorNorm)
            })
            .Where(x => x.Similarity >= minimum)
            .Select(x => new
            {
                Id = x.Document.ProductId,
                x.Similarity,
                Score = .8 * x.Similarity + .2 * (tokens.Count == 0
                    ? 0
                    : (double)tokens.Count(x.Document.Tokens.Contains) / tokens.Count)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .Take(plan.PreferBudget ? topK * 4 : topK)
            .ToArray();
        // Prices/stock are re-read from SQL, even when a cached embedding was used.
        var loadTimer = Stopwatch.StartNew();
        var live = (await repository.LoadAsync(ranked.Select(x => x.Id), plan, ct)).ToDictionary(x => x.Id);
        loadTimer.Stop();
        var matches = ranked.Where(x => live.ContainsKey(x.Id)).Select(x =>
            new ProductMatch(live[x.Id], Math.Round(x.Similarity, 4), Math.Round(x.Score, 4)));
        matches = plan.PreferBudget ? matches.OrderBy(x => x.Product.Price).ThenByDescending(x => x.Score) : matches;
        return new RetrievalResult(
            matches.Take(topK).ToArray(),
            eligible.Length,
            snapshot.UpdatedAtUtc,
            "hybrid-vector-lexical",
            sqlFilterMs,
            embeddingTimer.Elapsed.TotalMilliseconds,
            loadTimer.Elapsed.TotalMilliseconds);
    }

    public static double Cosine(float[] a, float[] b, double normA, double normB)
    {
        if (a.Length == 0 || a.Length != b.Length || normA <= 0 || normB <= 0)
        {
            return 0;
        }

        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
        }

        return dot / (normA * normB);
    }
}
