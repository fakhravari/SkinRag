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
            await db.Profiles.AsNoTracking().ToArrayAsync(ct),
            await db.Concerns.AsNoTracking().ToArrayAsync(ct),
            await db.Ingredients.AsNoTracking().ToArrayAsync(ct),
            await db.Products.AsNoTracking().Where(x => x.IsActive).SelectMany(x => x.Variants)
                .Where(x => x.IsActive && x.Shade != null).Select(x => x.Shade!).Distinct().ToArrayAsync(ct),
            await db.Products.AsNoTracking().Where(x => x.IsActive).SelectMany(x => x.Variants)
                .Where(x => x.IsActive && x.Finish != null).Select(x => x.Finish!).Distinct().ToArrayAsync(ct));
    }

    public async Task<int[]> EligibleIdsAsync(SearchPlan plan, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await CatalogQueryService.ValidateFiltersAsync(db, plan.Filters, ct);
        var scope = await CatalogQueryService.ResolveCategoryScopeAsync(db, plan.Filters, ct);
        return await Query(db, plan, scope).Select(p => p.Id).ToArrayAsync(ct);
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
        foreach (var slug in plan.ConcernSlugs)
        {
            query = query.Where(p => p.ProductConcerns.Any(x => x.Concern.Slug == slug));
        }

        if (plan.ProductIds.Length > 0)
        {
            query = query.Where(p => plan.ProductIds.Contains(p.Id));
        }

        return query;
    }
}
