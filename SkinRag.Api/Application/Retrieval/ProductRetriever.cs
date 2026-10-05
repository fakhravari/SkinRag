using System.Diagnostics;
using Microsoft.Extensions.AI;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Common.AI;
using SkinRag.Api.Application.Common.Text;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Intent;

namespace SkinRag.Api.Application.Retrieval;

public sealed class ProductRetriever(
    IProductRepository repository,
    IKnowledgeIndex index,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IConfiguration configuration,
    ILogger<ProductRetriever> logger) : IProductRetriever
{
    public async Task<RetrievalResult> RetrieveAsync(SearchPlan plan, CancellationToken ct)
    {
        var hasVariantConstraint = HasVariantConstraint(plan.Filters);
        var sqlTimer = Stopwatch.StartNew();
        var eligible = await repository.EligibleIdsAsync(plan, ct);
        sqlTimer.Stop();
        var sqlFilterMs = sqlTimer.Elapsed.TotalMilliseconds;
        if (eligible.Length == 0)
        {
            LogVariantRetrieval(plan, hasVariantConstraint, [], []);
            var diagnosisTimer = Stopwatch.StartNew();
            var filterDiagnosis = await repository.DiagnoseZeroResultsAsync(plan, ct);
            diagnosisTimer.Stop();
            sqlFilterMs += diagnosisTimer.Elapsed.TotalMilliseconds;
            var restoringFilter = filterDiagnosis.IndividualRelaxations
                .FirstOrDefault(x => x.RemainingProducts > 0)?.Filter;
            var cumulativeRestoringFilter = filterDiagnosis.CumulativeRelaxations
                .FirstOrDefault(x => x.RemainingProducts > 0)?.Filter;
            if (plan.Intent is ConsultationIntent.ProductSearch or ConsultationIntent.SkinConsultation
                && plan.ProductIds.Length == 0
                && filterDiagnosis.IndividualRelaxations.Any(x => x.Filter == "brand" && x.RemainingProducts > 0))
            {
                var alternativeFilters = QueryBuilder.CopyFilters(plan.Filters);
                alternativeFilters.BrandSlug = null;
                alternativeFilters.BrandSlugs = [];
                var alternativePlan = plan with { Filters = alternativeFilters };
                var alternatives = await RetrieveAsync(alternativePlan, ct);
                if (alternatives.Products.Count > 0)
                {
                    return alternatives with
                    {
                        SqlFilterMs = sqlFilterMs + alternatives.SqlFilterMs,
                        Diagnostics = new RetrievalDiagnostics(
                            "sql-filters",
                            "requested-brand-unavailable-alternatives",
                            "brand",
                            filterDiagnosis,
                            EligibleCount: alternatives.EligibleProducts),
                        EffectivePlan = alternativePlan
                    };
                }
            }

            var diagnostics = new RetrievalDiagnostics(
                "sql-filters",
                restoringFilter is not null ? "single-filter-relaxation-restores-results"
                    : cumulativeRestoringFilter is not null ? "combined-filter-relaxation-restores-results"
                    : "no-filter-relaxation-restores-results",
                restoringFilter ?? cumulativeRestoringFilter,
                filterDiagnosis,
                EligibleCount: 0);
            return new RetrievalResult([], 0, index.Snapshot().UpdatedAtUtc, "sql-filters", sqlFilterMs,
                EligibleProductIds: [], Diagnostics: diagnostics);
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
                ProductLoadMs: directLoadTimer.Elapsed.TotalMilliseconds,
                EligibleProductIds: eligible);
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
            LogVariantRetrieval(plan, hasVariantConstraint, eligible, []);
            return new RetrievalResult([], eligible.Length, snapshot.UpdatedAtUtc, "hybrid-vector-lexical",
                EligibleProductIds: eligible,
                Diagnostics: new RetrievalDiagnostics("knowledge-index", "eligible-products-missing-from-index",
                    EligibleCount: eligible.Length, IndexCandidateCount: 0));
        }

        IReadOnlyDictionary<int, int> concernMatchCounts = new Dictionary<int, int>();
        if (plan.InferredConcernSlugs.Length > 0)
        {
            var concernTimer = Stopwatch.StartNew();
            concernMatchCounts = await repository.CountConcernMatchesAsync(eligible, plan.InferredConcernSlugs, ct);
            concernTimer.Stop();
            sqlFilterMs += concernTimer.Elapsed.TotalMilliseconds;
        }

        var inferredProfiles = plan.InferredProfileSlugs ?? [];
        IReadOnlyDictionary<int, int> profileMatchCounts = new Dictionary<int, int>();
        if (inferredProfiles.Length > 0)
        {
            var profileTimer = Stopwatch.StartNew();
            profileMatchCounts = await repository.CountProfileMatchesAsync(eligible, inferredProfiles, ct);
            profileTimer.Stop();
            sqlFilterMs += profileTimer.Elapsed.TotalMilliseconds;
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
        var concernMatchBoost = configuration.GetValue("Rag:ConcernMatchBoost", .05);
        var profileMatchBoost = configuration.GetValue("Rag:ProfileMatchBoost", .05);
        var topK = Math.Clamp(configuration.GetValue("Rag:TopK", 5), 1, 10);
        var similarityCandidates = candidates.Count(x => hasVariantConstraint
            || Cosine(vector, x.Embedding, norm, x.VectorNorm) >= minimum);
        var ranked = candidates.Select(d => new
            {
                Document = d,
                Similarity = Cosine(vector, d.Embedding, norm, d.VectorNorm)
            })
            // An exact SQL shade/finish filter is stronger evidence than the
            // embedding score. Keep every exact variant candidate for ranking.
            .Where(x => hasVariantConstraint || x.Similarity >= minimum)
            .Select(x => new
            {
                Id = x.Document.ProductId,
                x.Similarity,
                ConcernMatches = concernMatchCounts.GetValueOrDefault(x.Document.ProductId),
                ProfileMatches = profileMatchCounts.GetValueOrDefault(x.Document.ProductId),
                LexicalScore = tokens.Count == 0
                    ? 0
                    : (double)tokens.Count(x.Document.Tokens.Contains) / tokens.Count
            })
            .Select(x => new
            {
                x.Id,
                x.Similarity,
                Score = Math.Min(1, (hasVariantConstraint
                    ? .55 * x.Similarity + .45 * x.LexicalScore
                    : .8 * x.Similarity + .2 * x.LexicalScore)
                    + concernMatchBoost * x.ConcernMatches / Math.Max(1, plan.InferredConcernSlugs.Length)
                    + profileMatchBoost * x.ProfileMatches / Math.Max(1, inferredProfiles.Length))
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .Take(plan.PreferBudget ? topK * 4 : topK)
            .ToArray();
        if (ranked.Length == 0)
        {
            return new RetrievalResult([], eligible.Length, snapshot.UpdatedAtUtc, "hybrid-vector-lexical",
                sqlFilterMs, embeddingTimer.Elapsed.TotalMilliseconds,
                EligibleProductIds: eligible,
                Diagnostics: new RetrievalDiagnostics("similarity", "all-index-candidates-below-threshold",
                    EligibleCount: eligible.Length, IndexCandidateCount: candidates.Length,
                    SimilarityCandidateCount: similarityCandidates));
        }
        // Prices/stock are re-read from SQL, even when a cached embedding was used.
        var loadTimer = Stopwatch.StartNew();
        var live = (await repository.LoadAsync(ranked.Select(x => x.Id), plan, ct)).ToDictionary(x => x.Id);
        loadTimer.Stop();
        var matches = ranked.Where(x => live.ContainsKey(x.Id)).Select(x =>
            new ProductMatch(live[x.Id], Math.Round(x.Similarity, 4), Math.Round(x.Score, 4)));
        var liveMatchCount = live.Count;
        matches = plan.PreferBudget ? matches.OrderBy(x => x.Product.Price).ThenByDescending(x => x.Score) : matches;
        var selectedMatches = matches.Take(topK).ToArray();
        LogVariantRetrieval(plan, hasVariantConstraint, eligible, selectedMatches.Select(x => x.Product.Id).ToArray());
        return new RetrievalResult(
            selectedMatches,
            eligible.Length,
            snapshot.UpdatedAtUtc,
            "hybrid-vector-lexical",
            sqlFilterMs,
            embeddingTimer.Elapsed.TotalMilliseconds,
            loadTimer.Elapsed.TotalMilliseconds,
            eligible,
            selectedMatches.Length == 0
                ? new RetrievalDiagnostics("live-product-load", "products-became-ineligible-before-response",
                    EligibleCount: eligible.Length, IndexCandidateCount: candidates.Length,
                    SimilarityCandidateCount: similarityCandidates, LiveProductCount: liveMatchCount)
                : null);
    }

    private static bool HasVariantConstraint(CatalogFilters filters) =>
        !string.IsNullOrWhiteSpace(filters.Shade)
        || (filters.Shades?.Any(x => !string.IsNullOrWhiteSpace(x)) ?? false)
        || !string.IsNullOrWhiteSpace(filters.Finish)
        || (filters.Finishes?.Any(x => !string.IsNullOrWhiteSpace(x)) ?? false);

    private void LogVariantRetrieval(SearchPlan plan, bool hasVariantConstraint, int[] eligibleIds,
        int[] returnedIds)
    {
        if (!hasVariantConstraint)
        {
            return;
        }

        logger.LogInformation(
            "Exact variant retrieval for {CategorySlug}: shade {Shade}/{Shades}, finish {Finish}/{Finishes}; " +
            "eligible product IDs {EligibleProductIds}; returned product IDs {ReturnedProductIds}",
            plan.Filters.CategorySlug,
            plan.Filters.Shade,
            plan.Filters.Shades,
            plan.Filters.Finish,
            plan.Filters.Finishes,
            eligibleIds,
            returnedIds);
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
