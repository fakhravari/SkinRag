using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Domain.Catalog;
using SkinRag.Api.Infrastructure.Persistence;

namespace SkinRag.Api.Infrastructure.Catalog;

public sealed class CatalogQueryService(IDbContextFactory<SkinRagDbContext> dbFactory) : ICatalogQueryService
{
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
        var products = await Hydrate(query.OrderBy(p => p.Id).Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize))
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
        var product = await Hydrate(db.Products.AsNoTracking().Where(p => p.Id == id && p.IsActive))
            .SingleOrDefaultAsync(ct);
        return product == null ? null : ToDto(product, new CatalogFilters(), false);
    }

    public async Task<CatalogFilterOptions> GetFiltersAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new CatalogFilterOptions(
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
            await db.ProductVariants.AsNoTracking().Where(x => x.IsActive && x.Shade != null).Select(x => x.Shade!)
                .Distinct().OrderBy(x => x).ToArrayAsync(ct),
            await db.ProductVariants.AsNoTracking().Where(x => x.IsActive && x.Finish != null).Select(x => x.Finish!)
                .Distinct().OrderBy(x => x).ToArrayAsync(ct));
    }

    public async Task<CatalogStatistics> GetStatisticsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var products = db.Products.AsNoTracking();
        var domains = await products.Where(p => p.CategoryDetails != null)
            .GroupBy(p => p.CategoryDetails!.Domain)
            .Select(g => new CatalogDomainStatistics(g.Key, g.Count(), g.Count(p => p.IsActive)))
            .ToArrayAsync(ct);

        return new CatalogStatistics(
            await products.CountAsync(ct),
            await products.CountAsync(p => p.IsActive, ct),
            await Filter(products, new CatalogFilters()).CountAsync(ct),
            await db.ProductVariants.CountAsync(ct),
            await db.Categories.CountAsync(ct),
            await db.Brands.CountAsync(ct),
            domains);
    }

    public static async Task<CategoryScope?> ResolveCategoryScopeAsync(
        SkinRagDbContext db,
        CatalogFilters filters,
        CancellationToken ct)
    {
        var domains = filters.Domains.Append(filters.Domain).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var categorySlugs = filters.CategorySlugs.Append(filters.CategorySlug)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (domains.Length == 0 && categorySlugs.Length == 0)
        {
            return null;
        }

        var categories = await db.Categories.AsNoTracking().Select(x => new { x.Id, x.ParentId, x.Slug, x.Domain })
            .ToListAsync(ct);

        int[] Expand(IEnumerable<int> roots)
        {
            var included = roots.ToHashSet();
            var frontier = included.ToArray();
            while (frontier.Length > 0)
            {
                frontier = categories
                    .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value) && included.Add(x.Id))
                    .Select(x => x.Id).ToArray();
            }

            return included.ToArray();
        }

        var domainIds = domains.Length == 0
            ? null
            : categories.Where(x => domains.Contains(x.Domain, StringComparer.OrdinalIgnoreCase)).Select(x => x.Id).ToArray();
        int[]? categoryIds = null;
        foreach (var categorySlug in categorySlugs)
        {
            var selectedCategory = categories.FirstOrDefault(x => x.Slug == categorySlug);
            var selectedIds = selectedCategory is null ? [] : Expand([selectedCategory.Id]);

            // Some imported top-level categories are siblings of the normalized
            // taxonomy instead of parents. A category whose slug is its domain
            // still means the whole domain (for example, category "hair").
            if (selectedCategory is not null && selectedCategory.Slug == selectedCategory.Domain)
            {
                selectedIds = categories.Where(x => x.Domain == selectedCategory.Domain).Select(x => x.Id).ToArray();
            }
            // In the imported tree, some child categories kept their source
            // hierarchy while their products were assigned a normalized domain.
            // Keep the selected category subtree available when its own domain
            // matches the selected domain.
            else if (selectedCategory is not null && domains.Contains(selectedCategory.Domain, StringComparer.OrdinalIgnoreCase)
                     && domainIds is not null)
            {
                domainIds = domainIds.Concat(selectedIds).Distinct().ToArray();
            }

            categoryIds = categoryIds is null ? selectedIds : categoryIds.Concat(selectedIds).Distinct().ToArray();
        }

        return new CategoryScope(domainIds, categoryIds);
    }

    public static IQueryable<Product> Hydrate(IQueryable<Product> query)
    {
        return query.Include(p => p.CategoryDetails).ThenInclude(c => c!.Parent)
            .Include(p => p.BrandDetails)
            .Include(p => p.Variants)
            .Include(p => p.ProductProfiles)
            .ThenInclude(x => x.Profile)
            .Include(p => p.ProductConcerns)
            .ThenInclude(x => x.Concern)
            .Include(p => p.ProductIngredients)
            .ThenInclude(x => x.Ingredient)
            .AsSplitQuery();
    }

    public static IQueryable<Product> Filter(IQueryable<Product> query, CatalogFilters f, bool inStockOnly = true,
        CategoryScope? scope = null)
    {
        query = query.Where(p => p.IsActive);
        var domains = f.Domains.Append(f.Domain).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var categorySlugs = f.CategorySlugs.Append(f.CategorySlug).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct().ToArray();
        var brandSlugs = f.BrandSlugs.Append(f.BrandSlug).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var skinTypes = f.SkinTypes.Append(f.SkinType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var hairTypes = f.HairTypes.Append(f.HairType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var concernSlugs = f.ConcernSlugs.Append(f.ConcernSlug).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var shades = f.Shades.Append(f.Shade).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var finishes = f.Finishes.Append(f.Finish).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (domains.Length > 0)
        {
            query = scope?.DomainCategoryIds is { } domainCategoryIds
                ? query.Where(p => domainCategoryIds.Contains(p.CategoryId))
                : query.Where(p => p.CategoryDetails != null && domains.Contains(p.CategoryDetails.Domain));
        }

        if (categorySlugs.Length > 0)
        {
            query = scope?.CategoryCategoryIds is { } categoryCategoryIds
                ? query.Where(p => categoryCategoryIds.Contains(p.CategoryId))
                : query.Where(p => p.CategoryDetails != null
                                   && (categorySlugs.Contains(p.CategoryDetails.Slug)
                                       || (p.CategoryDetails.Parent != null && categorySlugs.Contains(p.CategoryDetails.Parent.Slug))));
        }

        if (brandSlugs.Length > 0)
        {
            query = query.Where(p => p.BrandDetails != null && brandSlugs.Contains(p.BrandDetails.Slug));
        }

        if (skinTypes.Length > 0)
        {
            query = query.Where(p => p.ProductProfiles.Any(x => x.Profile.Kind == "skin"
                                                                && (skinTypes.Contains(x.Profile.Slug) ||
                                                                    (x.Profile.Name != null && skinTypes.Contains(x.Profile.Name)) ||
                                                                    x.Profile.Slug == "skin-all"))
                                     || (!p.ProductProfiles.Any() && skinTypes.Any(skinType =>
                                         p.SkinTypes.Contains(skinType!))));
        }

        if (hairTypes.Length > 0)
        {
            query = query.Where(p => p.ProductProfiles.Any(x => x.Profile.Kind == "hair"
                                                                && (hairTypes.Contains(x.Profile.Slug) ||
                                                                    (x.Profile.Name != null && hairTypes.Contains(x.Profile.Name)) ||
                                                                    x.Profile.Slug == "hair-all"))
                                     || (!p.ProductProfiles.Any() && hairTypes.Any(hairType =>
                                         p.HairTypes.Contains(hairType!))));
        }

        if (concernSlugs.Length > 0)
        {
            query = query.Where(p => p.ProductConcerns.Any(x => concernSlugs.Contains(x.Concern.Slug)));
        }

        if (f.FragranceFree.HasValue)
        {
            query = query.Where(p => p.FragranceFreeKnown && p.FragranceFree == f.FragranceFree);
        }

        if (f.ExcludeIngredientSlugs.Length > 0)
        {
            // Unknown legacy formulas are not eligible for an ingredient exclusion claim.
            query = query.Where(p =>
                p.ProductIngredients.Any() &&
                !p.ProductIngredients.Any(x => f.ExcludeIngredientSlugs.Contains(x.Ingredient.Slug)));
        }

        // Every condition must hold for the SAME variant; aggregate parent prices/stock are not authoritative.
        return query.Where(p => p.Variants.Any(v => v.IsActive && (!inStockOnly || v.StockQuantity > 0)
                                                               && (!f.MinPrice.HasValue || v.Price >= f.MinPrice)
                                                               && (!f.MaxPrice.HasValue || v.Price <= f.MaxPrice)
                                                               && (shades.Length == 0 || (v.Shade != null && shades.Contains(v.Shade)))
                                                               && (finishes.Length == 0 || (v.Finish != null && finishes.Contains(v.Finish)))
                                                               && (!f.SizeValue.HasValue || v.SizeValue == f.SizeValue)
                                                               && (f.SizeUnit == null || v.SizeUnit == f.SizeUnit))
                                || (!p.Variants.Any() && (!inStockOnly || p.StockQuantity > 0)
                                                      && (!f.MinPrice.HasValue || p.Price >= f.MinPrice)
                                                      && (!f.MaxPrice.HasValue || p.Price <= f.MaxPrice)
                                                      && shades.Length == 0
                                                      && finishes.Length == 0
                                                      && !f.SizeValue.HasValue
                                                      && f.SizeUnit == null));
    }

    public static IReadOnlyList<VariantDto> EligibleVariants(Product p, CatalogFilters f, bool inStockOnly = true)
    {
        var shades = f.Shades.Append(f.Shade).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var finishes = f.Finishes.Append(f.Finish).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return p.Variants.Where(v => v.IsActive && (!inStockOnly || v.StockQuantity > 0)
                                                && (!f.MinPrice.HasValue || v.Price >= f.MinPrice)
                                                && (!f.MaxPrice.HasValue || v.Price <= f.MaxPrice)
                                                && (shades.Length == 0 || (v.Shade != null && shades.Contains(v.Shade)))
                                                && (finishes.Length == 0 || (v.Finish != null && finishes.Contains(v.Finish)))
                                                && (!f.SizeValue.HasValue || v.SizeValue == f.SizeValue)
                                                && (f.SizeUnit == null || v.SizeUnit == f.SizeUnit))
            .OrderBy(v => v.Price)
            .ThenBy(v => v.Id)
            .Select(v => new VariantDto(v.Id, v.Sku, v.Name, v.SizeValue, v.SizeUnit, v.Shade, v.Finish, v.Price,
                v.StockQuantity))
            .ToArray();
    }

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
        var categorySlugs = f.CategorySlugs.Append(f.CategorySlug).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (categorySlugs.Length > 0 && await db.Categories.CountAsync(x => categorySlugs.Contains(x.Slug), ct) != categorySlugs.Distinct().Count())
        {
            throw new ArgumentException("دسته‌بندی ناشناخته است؛ فهرست معتبر در api/catalog/filters است.");
        }

        var brandSlugs = f.BrandSlugs.Append(f.BrandSlug).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (brandSlugs.Length > 0 && await db.Brands.CountAsync(x => brandSlugs.Contains(x.Slug), ct) != brandSlugs.Distinct().Count())
        {
            throw new ArgumentException("برند ناشناخته است.");
        }

        var concernSlugs = f.ConcernSlugs.Append(f.ConcernSlug).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (concernSlugs.Length > 0 && await db.Concerns.CountAsync(x => concernSlugs.Contains(x.Slug), ct) != concernSlugs.Distinct().Count())
        {
            throw new ArgumentException("نیاز مراقبتی ناشناخته است.");
        }

        var skinTypes = f.SkinTypes.Append(f.SkinType).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (skinTypes.Length > 0 && await db.Profiles.CountAsync(x => x.Kind == "skin" && skinTypes.Contains(x.Slug), ct) != skinTypes.Distinct().Count())
        {
            throw new ArgumentException("نوع پوست ناشناخته است؛ از نام یا slug پروفایل استفاده کنید.");
        }

        var hairTypes = f.HairTypes.Append(f.HairType).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (hairTypes.Length > 0 && await db.Profiles.CountAsync(x => x.Kind == "hair" && hairTypes.Contains(x.Slug), ct) != hairTypes.Distinct().Count())
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

        var domains = f.Domains.Append(f.Domain).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (domains.Length > 0 && await db.Categories.Select(x => x.Domain).Distinct().CountAsync(x => domains.Contains(x), ct) != domains.Distinct().Count())
            throw new ArgumentException("حوزهٔ انتخابی ناشناخته است.");
        var shades = f.Shades.Append(f.Shade).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (shades.Length > 0 && await db.ProductVariants.Where(x => x.IsActive && shades.Contains(x.Shade!)).Select(x => x.Shade).Distinct().CountAsync(ct) != shades.Distinct().Count())
            throw new ArgumentException("رنگ انتخابی ناشناخته است.");
        var finishes = f.Finishes.Append(f.Finish).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (finishes.Length > 0 && await db.ProductVariants.Where(x => x.IsActive && finishes.Contains(x.Finish!)).Select(x => x.Finish).Distinct().CountAsync(ct) != finishes.Distinct().Count())
            throw new ArgumentException("جلوهٔ انتخابی ناشناخته است.");
    }
}
