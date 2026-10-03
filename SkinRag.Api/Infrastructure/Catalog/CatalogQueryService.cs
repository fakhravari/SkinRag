using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Infrastructure.Persistence;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Contracts.Consultation;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Infrastructure.Catalog;

public sealed class CatalogQueryService(IDbContextFactory<SkinRagDbContext> dbFactory) : ICatalogQueryService
{
    public sealed record CategoryScope(int[]? DomainCategoryIds, int[]? CategoryCategoryIds);

    public static async Task<CategoryScope?> ResolveCategoryScopeAsync(
        SkinRagDbContext db,
        CatalogFilters filters,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filters.Domain) && string.IsNullOrWhiteSpace(filters.CategorySlug))
        {
            return null;
        }

        var categories = await db.Categories.AsNoTracking()
            .Select(x => new { x.Id, x.ParentId, x.Slug, x.Domain })
            .ToListAsync(ct);

        int[] Expand(IEnumerable<int> roots)
        {
            var included = roots.ToHashSet();
            var frontier = included.ToArray();
            while (frontier.Length > 0)
            {
                frontier = categories.Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value) && included.Add(x.Id))
                    .Select(x => x.Id)
                    .ToArray();
            }

            return included.ToArray();
        }

        var domainIds = string.IsNullOrWhiteSpace(filters.Domain)
            ? null
            : categories.Where(x => x.Domain == filters.Domain).Select(x => x.Id).ToArray();
        int[]? categoryIds = null;
        if (!string.IsNullOrWhiteSpace(filters.CategorySlug))
        {
            var selectedCategory = categories.FirstOrDefault(x => x.Slug == filters.CategorySlug);
            categoryIds = selectedCategory is null ? [] : Expand([selectedCategory.Id]);

            // Some imported top-level categories are siblings of the normalized
            // taxonomy instead of parents. A category whose slug is its domain
            // still means the whole domain (for example, category "hair").
            if (selectedCategory is not null && selectedCategory.Slug == selectedCategory.Domain)
            {
                categoryIds = domainIds ?? categories.Where(x => x.Domain == selectedCategory.Domain).Select(x => x.Id).ToArray();
            }
            // In the imported tree, some child categories kept their source
            // hierarchy while their products were assigned a normalized domain.
            // Keep the selected category subtree available when its own domain
            // matches the selected domain.
            else if (selectedCategory is not null && selectedCategory.Domain == filters.Domain && domainIds is not null)
            {
                domainIds = domainIds.Concat(categoryIds).Distinct().ToArray();
            }
        }
        return new(domainIds, categoryIds);
    }

    public static IQueryable<Product> Hydrate(IQueryable<Product> query) => query.Include(p => p.CategoryDetails).ThenInclude(c => c!.Parent).Include(p => p.BrandDetails)
        .Include(p => p.Variants)
        .Include(p => p.ProductProfiles)
        .ThenInclude(x => x.Profile)
        .Include(p => p.ProductConcerns)
        .ThenInclude(x => x.Concern)
        .Include(p => p.ProductIngredients)
        .ThenInclude(x => x.Ingredient)
        .AsSplitQuery();

    public static IQueryable<Product> Filter(IQueryable<Product> query, CatalogFilters f, bool inStockOnly = true, CategoryScope? scope = null)
    {
        query = query.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(f.Domain))
        {
            query = scope?.DomainCategoryIds is { } domainCategoryIds
                ? query.Where(p => domainCategoryIds.Contains(p.CategoryId))
                : query.Where(p => p.CategoryDetails != null && p.CategoryDetails.Domain == f.Domain);
        }

        if (!string.IsNullOrWhiteSpace(f.CategorySlug))
        {
            query = scope?.CategoryCategoryIds is { } categoryCategoryIds
                ? query.Where(p => categoryCategoryIds.Contains(p.CategoryId))
                : query.Where(p => p.CategoryDetails != null
                    && (p.CategoryDetails.Slug == f.CategorySlug
                    || (p.CategoryDetails.Parent != null && p.CategoryDetails.Parent.Slug == f.CategorySlug)));
        }

        if (!string.IsNullOrWhiteSpace(f.BrandSlug))
        {
            query = query.Where(p => p.BrandDetails != null && p.BrandDetails.Slug == f.BrandSlug);
        }

        if (!string.IsNullOrWhiteSpace(f.SkinType))
        {
            query = query.Where(p => p.ProductProfiles.Any(x => x.Profile.Kind == "skin"
                && (x.Profile.Slug == f.SkinType || x.Profile.Name == f.SkinType || x.Profile.Slug == "skin-all"))
                || (!p.ProductProfiles.Any() && p.SkinTypes.Contains(f.SkinType)));
        }

        if (!string.IsNullOrWhiteSpace(f.HairType))
        {
            query = query.Where(p => p.ProductProfiles.Any(x => x.Profile.Kind == "hair"
                && (x.Profile.Slug == f.HairType || x.Profile.Name == f.HairType || x.Profile.Slug == "hair-all"))
                || (!p.ProductProfiles.Any() && p.HairTypes.Contains(f.HairType)));
        }

        if (!string.IsNullOrWhiteSpace(f.ConcernSlug))
        {
            query = query.Where(p => p.ProductConcerns.Any(x => x.Concern.Slug == f.ConcernSlug));
        }

        if (f.FragranceFree.HasValue)
        {
            query = query.Where(p => p.FragranceFreeKnown && p.FragranceFree == f.FragranceFree);
        }

        if (f.ExcludeIngredientSlugs.Length > 0)
        {
            // Unknown legacy formulas are not eligible for an ingredient exclusion claim.
            query = query.Where(p => p.ProductIngredients.Any()
                && !p.ProductIngredients.Any(x => f.ExcludeIngredientSlugs.Contains(x.Ingredient.Slug)));
        }
        // Every condition must hold for the SAME variant; aggregate parent prices/stock are not authoritative.
        return query.Where(p => p.Variants.Any(v => v.IsActive && (!inStockOnly || v.StockQuantity > 0)
            && (!f.MinPrice.HasValue || v.Price >= f.MinPrice)
            && (!f.MaxPrice.HasValue || v.Price <= f.MaxPrice)
            && (f.Shade == null || v.Shade == f.Shade)
            && (f.Finish == null || v.Finish == f.Finish)
            && (!f.SizeValue.HasValue || v.SizeValue == f.SizeValue)
            && (f.SizeUnit == null || v.SizeUnit == f.SizeUnit))
            || (!p.Variants.Any() && (!inStockOnly || p.StockQuantity > 0)
            && (!f.MinPrice.HasValue || p.Price >= f.MinPrice)
            && (!f.MaxPrice.HasValue || p.Price <= f.MaxPrice)
            && f.Shade == null
            && f.Finish == null
            && !f.SizeValue.HasValue
            && f.SizeUnit == null));
    }

    public static IReadOnlyList<VariantDto> EligibleVariants(Product p, CatalogFilters f, bool inStockOnly = true) => p.Variants.Where(v => v.IsActive && (!inStockOnly || v.StockQuantity > 0)
        && (!f.MinPrice.HasValue || v.Price >= f.MinPrice)
        && (!f.MaxPrice.HasValue || v.Price <= f.MaxPrice)
        && (f.Shade == null || v.Shade == f.Shade)
        && (f.Finish == null || v.Finish == f.Finish)
        && (!f.SizeValue.HasValue || v.SizeValue == f.SizeValue)
        && (f.SizeUnit == null || v.SizeUnit == f.SizeUnit))
        .OrderBy(v => v.Price)
        .ThenBy(v => v.Id)
        .Select(v => new VariantDto(v.Id, v.Sku, v.Name, v.SizeValue, v.SizeUnit, v.Shade, v.Finish, v.Price, v.StockQuantity))
        .ToArray();

    public static ProductDto ToDto(Product p, CatalogFilters f, bool inStockOnly = true)
    {
        var variants = EligibleVariants(p, f, inStockOnly);
        decimal? price = p.Variants.Count > 0 ? variants.Count > 0 ? variants.Min(v => v.Price) : null : p.Price;
        var stock = p.Variants.Count > 0 ? variants.Sum(v => v.StockQuantity) : p.StockQuantity;
        return new ProductDto(
            p.Id,
            p.Sku,
            p.Name,
            p.BrandDetails?.Name ?? p.Brand,
            p.BrandDetails?.Slug,
            p.CategoryDetails?.Name ?? p.Category,
            p.CategoryDetails?.Slug,
            p.CategoryDetails?.Domain,
            price,
            p.Currency,
            stock,
            p.FragranceFreeKnown ? p.FragranceFree : null,
            p.SkinTypes,
            p.HairTypes,
            p.Description,
            p.Warnings,
            p.UsageInstructions,
            p.Image,
            p.ProductProfiles.Select(x => x.Profile.Slug).Order().ToArray(),
            p.ProductConcerns.Select(x => x.Concern.Slug).Order().ToArray(),
            p.ProductIngredients.Select(x => x.Ingredient.Slug).Order().ToArray(),
            variants,
            p.Ingredients,
            p.Concerns);
    }

    public static async Task ValidateFiltersAsync(SkinRagDbContext db, CatalogFilters f, CancellationToken ct)
    {
        if (f.CategorySlug != null && !await db.Categories.AnyAsync(x => x.Slug == f.CategorySlug, ct))
        {
            throw new ArgumentException("دسته‌بندی ناشناخته است؛ فهرست معتبر در api/catalog/filters است.");
        }

        if (f.BrandSlug != null && !await db.Brands.AnyAsync(x => x.Slug == f.BrandSlug, ct))
        {
            throw new ArgumentException("برند ناشناخته است.");
        }

        if (f.ConcernSlug != null && !await db.Concerns.AnyAsync(x => x.Slug == f.ConcernSlug, ct))
        {
            throw new ArgumentException("نیاز مراقبتی ناشناخته است.");
        }

        if (f.SkinType != null
            && !await db.Profiles.AnyAsync(x => x.Kind == "skin" && (x.Slug == f.SkinType || x.Name == f.SkinType), ct))
        {
            throw new ArgumentException("نوع پوست ناشناخته است؛ از نام یا slug پروفایل استفاده کنید.");
        }

        if (f.HairType != null
            && !await db.Profiles.AnyAsync(x => x.Kind == "hair" && (x.Slug == f.HairType || x.Name == f.HairType), ct))
        {
            throw new ArgumentException("نوع مو ناشناخته است؛ از نام یا slug پروفایل استفاده کنید.");
        }

        if (f.ExcludeIngredientSlugs.Length > 0)
        {
            var known = await db.Ingredients.Where(x => f.ExcludeIngredientSlugs.Contains(x.Slug)).Select(x => x.Slug)
                .ToListAsync(ct);
            if (known.Count != f.ExcludeIngredientSlugs.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                throw new ArgumentException("یک یا چند ترکیب مستثنا ناشناخته است.");
            }
        }
    }

    public async Task<CatalogPage> SearchAsync(CatalogSearchRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await ValidateFiltersAsync(db, request, ct);
        var scope = await ResolveCategoryScopeAsync(db, request, ct);
        var query = Filter(db.Products.AsNoTracking(), request, request.InStockOnly, scope);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query = query.Where(p => p.Name.Contains(request.Search)
                || p.SearchKeywords.Contains(request.Search)
                || p.Sku.Contains(request.Search));
        }

        var total = await query.CountAsync(ct);
        var products = await Hydrate(query.OrderBy(p => p.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize))
            .ToListAsync(ct);
        return new CatalogPage(
            request.Page,
            request.PageSize,
            total,
            products.Select(p => ToDto(p, request, request.InStockOnly)).ToArray());
    }

    public async Task<ProductDto?> GetAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await Hydrate(db.Products.AsNoTracking().Where(p => p.Id == id && p.IsActive)).SingleOrDefaultAsync(ct);
        return product == null ? null : ToDto(product, new CatalogFilters(), false);
    }

    public async Task<CatalogFilterOptions> GetFiltersAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new(
            "IRR",
            "ریال",
            await db.Categories.AsNoTracking().Select(x => x.Domain).Distinct().OrderBy(x => x).ToArrayAsync(ct),
            await db.Categories.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new CatalogCategoryOption(x.Id, x.Slug, x.Name, x.Domain, x.ParentId)).ToArrayAsync(ct),
            await db.Brands.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new CatalogBrandOption(x.Id, x.Slug, x.Name, x.Country)).ToArrayAsync(ct),
            await db.Profiles.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new CatalogProfileOption(x.Id, x.Slug, x.Name, x.Kind)).ToArrayAsync(ct),
            await db.Concerns.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new CatalogConcernOption(x.Id, x.Slug, x.Name, x.Domain)).ToArrayAsync(ct),
            await db.Ingredients.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new CatalogIngredientOption(x.Id, x.Slug, x.Name, x.InciName)).ToArrayAsync(ct),
            await db.ProductVariants.AsNoTracking().Where(x => x.IsActive && x.Shade != null)
                .Select(x => x.Shade!).Distinct().OrderBy(x => x).ToArrayAsync(ct),
            await db.ProductVariants.AsNoTracking().Where(x => x.IsActive && x.Finish != null)
                .Select(x => x.Finish!).Distinct().OrderBy(x => x).ToArrayAsync(ct));
    }

    public async Task<CatalogStatistics> GetStatisticsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var products = db.Products.AsNoTracking();
        var domains = await products.Where(p => p.CategoryDetails != null)
            .GroupBy(p => p.CategoryDetails!.Domain)
            .Select(g => new CatalogDomainStatistics(g.Key, g.Count(), g.Count(p => p.IsActive)))
            .ToArrayAsync(ct);
        return new(
            await products.CountAsync(ct),
            await products.CountAsync(p => p.IsActive, ct),
            await Filter(products, new CatalogFilters()).CountAsync(ct),
            await db.ProductVariants.CountAsync(ct),
            await db.Categories.CountAsync(ct),
            await db.Brands.CountAsync(ct),
            domains);
    }
}
