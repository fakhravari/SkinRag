using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Domain.Catalog;
using SkinRag.Api.Infrastructure.Catalog;

namespace SkinRag.Api.Infrastructure.Persistence;

public sealed class ProductRepository(IDbContextFactory<SkinRagDbContext> factory) : IProductRepository
{
    public async Task<CatalogVocabulary> VocabularyAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return new CatalogVocabulary(
            await db.Categories.AsNoTracking().ToArrayAsync(ct),
            await db.Brands.AsNoTracking().ToArrayAsync(ct),
            await db.CatalogProfiles.AsNoTracking().ToArrayAsync(ct),
            await db.Concerns.AsNoTracking().ToArrayAsync(ct),
            await db.Ingredients.AsNoTracking().ToArrayAsync(ct),
            await db.Products.AsNoTracking().Where(x => x.IsActive).SelectMany(x => x.Variants)
                .Where(x => x.IsActive && x.Shade != null).Select(x => x.Shade!).Distinct().ToArrayAsync(ct),
            await db.Products.AsNoTracking().Where(x => x.IsActive).SelectMany(x => x.Variants)
                .Where(x => x.IsActive && x.Finish != null).Select(x => x.Finish!).Distinct().ToArrayAsync(ct),
            await db.CatalogPhrases.AsNoTracking().Include(x => x.Category).Include(x => x.Concern)
                .Include(x => x.CatalogProfile)
                .Where(x => x.IsActive && x.MappingStatus == CatalogPhraseStatus.Product)
                .ToArrayAsync(ct));
    }

    public async Task<IReadOnlyList<CatalogPhrase>> IntentPhrasesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CatalogPhrases.AsNoTracking().Include(x => x.Category).Include(x => x.Concern)
            .Include(x => x.CatalogProfile)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.Id)
            .ToArrayAsync(ct);
    }

    public async Task<int[]> EligibleIdsAsync(SearchPlan plan, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await CatalogQueryService.ValidateFiltersAsync(db, plan.Filters, ct);
        var scope = await CatalogQueryService.ResolveCategoryScopeAsync(db, plan.Filters, ct);
        return await Query(db, plan, scope).Select(p => p.Id).ToArrayAsync(ct);
    }

    public async Task<ZeroResultFilterDiagnosis> DiagnoseZeroResultsAsync(SearchPlan plan,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var relaxations = new List<(string Name, Func<SearchPlan, SearchPlan> Relax)>();
        void AddFilter(string name, Action<CatalogFilters> clear)
        {
            relaxations.Add((name, current =>
            {
                var filters = QueryBuilder.CopyFilters(current.Filters);
                clear(filters);
                return current with { Filters = filters };
            }));
        }

        var filters = plan.Filters;
        if (filters.Domains.Length > 0 || filters.Domain is not null)
            AddFilter("domain", f => { f.Domain = null; f.Domains = []; });
        if (filters.CategorySlugs.Length > 0 || filters.CategorySlug is not null)
            AddFilter("category", f => { f.CategorySlug = null; f.CategorySlugs = []; });
        if (filters.BrandSlugs.Length > 0 || filters.BrandSlug is not null)
            AddFilter("brand", f => { f.BrandSlug = null; f.BrandSlugs = []; });
        if (filters.SkinTypes.Length > 0 || filters.SkinType is not null)
            AddFilter("skinProfile", f => { f.SkinType = null; f.SkinTypes = []; });
        if (filters.HairTypes.Length > 0 || filters.HairType is not null)
            AddFilter("hairProfile", f => { f.HairType = null; f.HairTypes = []; });
        if (filters.ConcernSlugs.Length > 0 || filters.ConcernSlug is not null)
        {
            AddFilter("concern", f => { f.ConcernSlug = null; f.ConcernSlugs = []; });
        }
        if (filters.MinPrice.HasValue || filters.MaxPrice.HasValue)
            AddFilter("priceRange", f => { f.MinPrice = null; f.MaxPrice = null; });
        if (filters.Shades.Length > 0 || filters.Shade is not null)
            AddFilter("shade", f => { f.Shade = null; f.Shades = []; });
        if (filters.Finishes.Length > 0 || filters.Finish is not null)
            AddFilter("finish", f => { f.Finish = null; f.Finishes = []; });
        if (filters.SizeValue.HasValue || filters.SizeUnit is not null)
            AddFilter("size", f => { f.SizeValue = null; f.SizeUnit = null; });
        if (filters.FragranceFree.HasValue)
            AddFilter("fragranceFree", f => f.FragranceFree = null);
        if (filters.ExcludeIngredientSlugs.Length > 0)
            AddFilter("excludedIngredients", f => f.ExcludeIngredientSlugs = []);
        if (filters.IncludeIngredientSlugs.Length > 0)
            AddFilter("includedIngredients", f => f.IncludeIngredientSlugs = []);
        if (plan.InStockOnly)
            relaxations.Add(("inStock", current => current with { InStockOnly = false }));
        if (plan.ProductIds.Length > 0)
            relaxations.Add(("productIds", current => current with { ProductIds = [] }));

        async Task<int> CountAsync(SearchPlan candidate)
        {
            await CatalogQueryService.ValidateFiltersAsync(db, candidate.Filters, ct);
            var candidateScope = await CatalogQueryService.ResolveCategoryScopeAsync(db, candidate.Filters, ct);
            return await Query(db, candidate, candidateScope).CountAsync(ct);
        }

        var individualCounts = new List<FilterRelaxationCount>(relaxations.Count);
        foreach (var (name, relax) in relaxations)
        {
            individualCounts.Add(new FilterRelaxationCount(name, await CountAsync(relax(plan))));
        }

        var cumulativeCounts = new List<FilterRelaxationCount>(relaxations.Count);
        if (individualCounts.All(x => x.RemainingProducts == 0))
        {
            var cumulativelyRelaxedPlan = plan;
            foreach (var (name, relax) in relaxations)
            {
                cumulativelyRelaxedPlan = relax(cumulativelyRelaxedPlan);
                var count = await CountAsync(cumulativelyRelaxedPlan);
                cumulativeCounts.Add(new FilterRelaxationCount(name, count));
                if (count > 0)
                {
                    break;
                }
            }
        }

        return new ZeroResultFilterDiagnosis(individualCounts, cumulativeCounts);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountConcernMatchesAsync(int[] productIds,
        string[] concernSlugs, CancellationToken ct)
    {
        if (productIds.Length == 0 || concernSlugs.Length == 0)
        {
            return new Dictionary<int, int>();
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<ProductConcern>().AsNoTracking()
            .Where(x => productIds.Contains(x.IdProduct) && concernSlugs.Contains(x.Concern.Slug))
            .GroupBy(x => x.IdProduct)
            .Select(group => new { ProductId = group.Key, MatchCount = group.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.MatchCount, ct);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountProfileMatchesAsync(int[] productIds,
        string[] profileSlugs, CancellationToken ct)
    {
        if (productIds.Length == 0 || profileSlugs.Length == 0)
        {
            return new Dictionary<int, int>();
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<ProductProfile>().AsNoTracking()
            .Where(x => productIds.Contains(x.IdProduct) && profileSlugs.Contains(x.CatalogProfile.Slug))
            .GroupBy(x => x.IdProduct)
            .Select(group => new { ProductId = group.Key, MatchCount = group.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.MatchCount, ct);
    }

    public async Task<IReadOnlyList<ProductDto>> LoadAsync(IEnumerable<int> ids, SearchPlan plan, CancellationToken ct)
    {
        var selected = ids.Distinct().Take(100).ToArray();
        await using var db = await factory.CreateDbContextAsync(ct);
        await CatalogQueryService.ValidateFiltersAsync(db, plan.Filters, ct);
        var scope = await CatalogQueryService.ResolveCategoryScopeAsync(db, plan.Filters, ct);
        var rows = await CatalogQueryService.Hydrate(Query(db, plan, scope).Where(p => selected.Contains(p.Id)))
            .ToListAsync(ct);
        return rows.Select(p => CatalogQueryService.ToDto(p, plan.Filters, plan.InStockOnly)).ToArray();
    }

    private static IQueryable<Product> Query(SkinRagDbContext db, SearchPlan plan, CategoryScope? scope)
    {
        var query = CatalogQueryService.Filter(db.Products.AsNoTracking(), plan.Filters, plan.InStockOnly, scope);

        if (plan.ProductIds.Length > 0)
        {
            query = query.Where(p => plan.ProductIds.Contains(p.Id));
        }

        return query;
    }
}
