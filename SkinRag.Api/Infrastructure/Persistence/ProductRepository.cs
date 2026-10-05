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
        return await Query(db, plan, scope, await ConcernNamesAsync(db, plan, ct)).Select(p => p.Id).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ProductDto>> LoadAsync(IEnumerable<int> ids, SearchPlan plan, CancellationToken ct)
    {
        var selected = ids.Distinct().Take(100).ToArray();
        await using var db = await factory.CreateDbContextAsync(ct);
        await CatalogQueryService.ValidateFiltersAsync(db, plan.Filters, ct);
        var scope = await CatalogQueryService.ResolveCategoryScopeAsync(db, plan.Filters, ct);
        var rows = await CatalogQueryService.Hydrate(Query(db, plan, scope, await ConcernNamesAsync(db, plan, ct)).Where(p => selected.Contains(p.Id)))
            .ToListAsync(ct);
        return rows.Select(p => CatalogQueryService.ToDto(p, plan.Filters, plan.InStockOnly)).ToArray();
    }

    private static async Task<Dictionary<string, string>> ConcernNamesAsync(SkinRagDbContext db, SearchPlan plan,
        CancellationToken ct)
    {
        if (plan.ConcernSlugs.Length == 0)
        {
            return [];
        }

        return await db.Concerns.AsNoTracking().Where(x => plan.ConcernSlugs.Contains(x.Slug))
            .ToDictionaryAsync(x => x.Slug, x => x.Name, ct);
    }

    private static IQueryable<Product> Query(SkinRagDbContext db, SearchPlan plan, CategoryScope? scope,
        IReadOnlyDictionary<string, string> concernNames)
    {
        var query = CatalogQueryService.Filter(db.Products.AsNoTracking(), plan.Filters, plan.InStockOnly, scope);
        foreach (var slug in plan.ConcernSlugs)
        {
            // Concern links are optional data: a product without any link stays searchable
            // and is matched by its descriptive text instead of being dropped.
            var name = concernNames.GetValueOrDefault(slug);
            query = string.IsNullOrWhiteSpace(name)
                ? query.Where(p => p.ProductConcerns.Any(x => x.Concern.Slug == slug))
                : query.Where(p => p.ProductConcerns.Any(x => x.Concern.Slug == slug)
                                   || (!p.ProductConcerns.Any()
                                       && (p.Concerns.Contains(name) || p.SearchKeywords.Contains(name))));
        }

        if (plan.ProductIds.Length > 0)
        {
            query = query.Where(p => plan.ProductIds.Contains(p.Id));
        }

        return query;
    }
}
