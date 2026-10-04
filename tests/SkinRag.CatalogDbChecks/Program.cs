using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Infrastructure.Catalog;
using SkinRag.Api.Infrastructure.Persistence;

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "SkinRag.Api", "appsettings.json"))
    .AddEnvironmentVariables()
    .Build();
var connectionString = config.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
var options = new DbContextOptionsBuilder<SkinRagDbContext>().UseSqlServer(connectionString).Options;
var factory = new ReadOnlyContextFactory(options);
var catalog = new CatalogQueryService(factory);
var checks = 0;

void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    checks++;
}

await using (var db = await factory.CreateDbContextAsync())
{
    Check(await db.Database.CanConnectAsync(), "SQL Server is not reachable.");
    await db.Database.OpenConnectionAsync();
    var liveColumns = new HashSet<(string Schema, string Table, string Column)>();
    await using (var command = db.Database.GetDbConnection().CreateCommand())
    {
        command.CommandText = """
            SELECT s.name, t.name, c.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            liveColumns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
    }

    var missingColumns = db.Model.GetEntityTypes()
        .SelectMany(entity =>
        {
            var table = entity.GetTableName();
            if (table is null) return [];
            var schema = entity.GetSchema() ?? "dbo";
            var store = StoreObjectIdentifier.Table(table, schema);
            return entity.GetProperties()
                .Select(property => (Schema: schema, Table: table, Column: property.GetColumnName(store)))
                .Where(mapping => mapping.Column is not null)
                .Select(mapping => (mapping.Schema, mapping.Table, Column: mapping.Column!));
        })
        .Where(mapping => !liveColumns.Contains(mapping))
        .ToArray();
    Check(missingColumns.Length == 0,
        "EF mappings reference missing SQL columns: " + string.Join(", ", missingColumns.Select(x => $"{x.Table}.{x.Column}")));

    async Task<int> ScalarAsync(string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    Check(await ScalarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1") == 0,
        "A SQL foreign key is disabled or untrusted.");
    Check(await ScalarAsync("""
        SELECT COUNT(*) FROM dbo.Products p
        LEFT JOIN dbo.Categories c ON c.Id = p.IdCategory
        LEFT JOIN dbo.Brands b ON b.Id = p.IdBrand
        WHERE c.Id IS NULL OR b.Id IS NULL;
        """) == 0, "Products contain a missing category or brand reference.");
    Check(await ScalarAsync("""
        SELECT COUNT(*) FROM dbo.ProductVariants v
        LEFT JOIN dbo.Products p ON p.Id = v.IdProduct
        WHERE p.Id IS NULL;
        """) == 0, "Product variants contain an orphan product reference.");
    Check(await ScalarAsync("""
        SELECT COUNT(*) FROM
        (SELECT IdCategory, Phrase, SearchTerms FROM dbo.CatalogPhrases
         GROUP BY IdCategory, Phrase, SearchTerms HAVING COUNT(*) > 1) d;
        """) == 0, "Catalog phrase mappings contain duplicates.");
    Check(await ScalarAsync("""
        SELECT COUNT(*) FROM dbo.ProductEmbeddings e
        WHERE ISJSON(e.VectorJson) <> 1 OR e.Dimensions <= 0
           OR (SELECT COUNT(*) FROM OPENJSON(e.VectorJson)) <> e.Dimensions;
        """) == 0, "An embedding vector is invalid or has the wrong dimension count.");
    await db.Database.CloseConnectionAsync();
}

var filters = await catalog.GetFiltersAsync(default);
var stats = await catalog.GetStatisticsAsync(default);
var sizeOptions = await LoadSizeOptionsAsync(factory);
Check(filters.Currency == "IRR", "Catalog currency does not match the API contract.");
Check(filters.Categories.Select(x => x.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() == filters.Categories.Count,
    "Category slugs are duplicated.");
Check(filters.Brands.Select(x => x.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() == filters.Brands.Count,
    "Brand slugs are duplicated.");
Check(stats.TotalProducts >= stats.ActiveProducts && stats.ActiveProducts >= stats.AvailableProducts,
    "Catalog product totals are inconsistent.");
Check(stats.Domains.Sum(x => x.Products) == stats.TotalProducts
      && stats.Domains.Sum(x => x.ActiveProducts) == stats.ActiveProducts,
    "Per-domain totals do not reconcile with catalog totals.");

var firstPage = await catalog.SearchAsync(new CatalogSearchRequest { PageSize = 20 }, default);
Check(firstPage.Total == stats.AvailableProducts && firstPage.Items.Count <= 20,
    "Unfiltered catalog result does not reconcile with available-product statistics.");
Check(firstPage.Items.Select(x => x.Id).Distinct().Count() == firstPage.Items.Count,
    "The catalog page contains duplicate product IDs.");
Check(firstPage.Items.All(x => x.StockQuantity > 0), "In-stock search returned a sold-out product.");
if (firstPage.Items.Count > 0)
{
    var product = await catalog.GetAsync(firstPage.Items[0].Id, default);
    Check(product?.Id == firstPage.Items[0].Id, "Catalog product detail did not return the selected product.");
    Check(await catalog.GetAsync(int.MaxValue, default) is null, "Unknown product ID did not return null.");

    var textSearch = await catalog.SearchAsync(new CatalogSearchRequest
    {
        Search = firstPage.Items[0].Name,
        InStockOnly = false,
        PageSize = 100
    }, default);
    Check(textSearch.Items.Any(x => x.Id == firstPage.Items[0].Id), "Name search omitted its exact matching product.");

    var exactPrice = firstPage.Items[0].Price;
    if (exactPrice.HasValue)
    {
        var pricePage = await catalog.SearchAsync(new CatalogSearchRequest
        {
            MinPrice = exactPrice,
            MaxPrice = exactPrice,
            InStockOnly = true,
            PageSize = 100
        }, default);
        Check(pricePage.Items.All(x => x.Price == exactPrice), "Price filter returned a product outside the exact price.");
    }
}

foreach (var domain in filters.Domains)
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest { Domain = domain, InStockOnly = false, PageSize = 100 }, default);
    Check(result.Items.All(x => string.Equals(x.Domain, domain, StringComparison.OrdinalIgnoreCase)),
        $"Domain filter leaked products from another domain: {domain}");

    var duplicateCase = await catalog.SearchAsync(new CatalogSearchRequest
    {
        Domains = [domain, domain.ToUpperInvariant()],
        InStockOnly = false,
        PageSize = 100
    }, default);
    Check(result.Total == duplicateCase.Total, $"Duplicate case variants changed the result count for domain {domain}.");
}

foreach (var category in filters.Categories)
{
    var exact = await catalog.SearchAsync(new CatalogSearchRequest
    {
        CategorySlug = category.Slug,
        InStockOnly = false,
        PageSize = 1
    }, default);
    var differentlyCased = await catalog.SearchAsync(new CatalogSearchRequest
    {
        CategorySlug = category.Slug.ToUpperInvariant(),
        InStockOnly = false,
        PageSize = 1
    }, default);
    Check(exact.Total == differentlyCased.Total,
        $"Category slug casing changed the result count for {category.Slug}.");

    var duplicatedCasing = await catalog.SearchAsync(new CatalogSearchRequest
    {
        CategorySlugs = [category.Slug, category.Slug.ToUpperInvariant()],
        InStockOnly = false,
        PageSize = 1
    }, default);
    Check(exact.Total == duplicatedCasing.Total,
        $"Duplicate case variants changed the result count for {category.Slug}.");

    var categoryAndDomain = await catalog.SearchAsync(new CatalogSearchRequest
    {
        CategorySlug = category.Slug,
        Domain = category.Domain,
        InStockOnly = false,
        PageSize = 1
    }, default);
    Check(exact.Total == categoryAndDomain.Total,
        $"Combining a category with its own domain changed the result set for {category.Slug}.");
}

foreach (var brand in filters.Brands)
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest { BrandSlug = brand.Slug, InStockOnly = false, PageSize = 100 }, default);
    Check(result.Items.All(x => string.Equals(x.BrandSlug, brand.Slug, StringComparison.OrdinalIgnoreCase)),
        $"Brand filter leaked products from another brand: {brand.Slug}");
}

foreach (var concern in filters.Concerns)
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest { ConcernSlug = concern.Slug, InStockOnly = false, PageSize = 100 }, default);
    Check(result.Items.All(x => x.Concerns.Contains(concern.Slug, StringComparer.OrdinalIgnoreCase)),
        $"Concern filter leaked unrelated products: {concern.Slug}");
}

foreach (var profile in filters.Profiles)
{
    var request = profile.Kind == "skin"
        ? new CatalogSearchRequest { SkinType = profile.Slug, InStockOnly = false, PageSize = 100 }
        : new CatalogSearchRequest { HairType = profile.Slug, InStockOnly = false, PageSize = 100 };
    var result = await catalog.SearchAsync(request, default);
    Check(result.Items.All(x => profile.Kind == "skin"
            ? x.Domain is "skin" or "beauty"
            : x.Domain == "hair"),
        $"Profile filter returned an unrelated catalog domain: {profile.Slug}");
}

foreach (var shade in filters.Shades)
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest { Shade = shade, InStockOnly = false, PageSize = 100 }, default);
    Check(result.Items.All(x => x.Variants.Count > 0 && x.Variants.All(v => v.Shade == shade)),
        $"Shade filter returned an ineligible variant: {shade}");
}

foreach (var finish in filters.Finishes)
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest { Finish = finish, InStockOnly = false, PageSize = 100 }, default);
    Check(result.Items.All(x => x.Variants.Count > 0 && x.Variants.All(v => v.Finish == finish)),
        $"Finish filter returned an ineligible variant: {finish}");
}

foreach (var freeOfFragrance in new[] { true, false })
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest
    {
        FragranceFree = freeOfFragrance,
        InStockOnly = false,
        PageSize = 100
    }, default);
    Check(result.Items.All(x => x.FragranceFree == freeOfFragrance),
        $"Fragrance-known filter returned a conflicting or unknown value: {freeOfFragrance}");
}

foreach (var size in sizeOptions)
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest
    {
        SizeValue = size.SizeValue,
        SizeUnit = size.SizeUnit,
        InStockOnly = false,
        PageSize = 100
    }, default);
    Check(result.Items.All(x => x.Variants.Count > 0
        && x.Variants.All(v => v.SizeValue == size.SizeValue && v.SizeUnit == size.SizeUnit)),
        $"Size filter returned an ineligible variant: {size.SizeValue} {size.SizeUnit}");
}

foreach (var ingredient in filters.Ingredients)
{
    var result = await catalog.SearchAsync(new CatalogSearchRequest
    {
        ExcludeIngredientSlugs = [ingredient.Slug],
        InStockOnly = false,
        PageSize = 100
    }, default);
    Check(result.Items.All(x => !x.Ingredients.Contains(ingredient.Slug, StringComparer.OrdinalIgnoreCase)),
        $"Excluded ingredient appeared in results: {ingredient.Slug}");
}

Console.WriteLine($"{checks} read-only catalog/database checks passed. "
    + $"Products={stats.TotalProducts}, active={stats.ActiveProducts}, available={stats.AvailableProducts}, "
    + $"categories={stats.Categories}, variants={stats.Variants}.");

static async Task<(decimal SizeValue, string SizeUnit)[]> LoadSizeOptionsAsync(
    IDbContextFactory<SkinRagDbContext> factory)
{
    await using var db = await factory.CreateDbContextAsync();
    var rows = await db.ProductVariants.AsNoTracking().Where(x => x.IsActive)
        .Select(x => new { x.SizeValue, x.SizeUnit }).Distinct().Take(5).ToArrayAsync();
    return rows.Select(x => (x.SizeValue, x.SizeUnit)).ToArray();
}

sealed class ReadOnlyContextFactory(DbContextOptions<SkinRagDbContext> options) : IDbContextFactory<SkinRagDbContext>
{
    public SkinRagDbContext CreateDbContext() => new(options);

    public Task<SkinRagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}
