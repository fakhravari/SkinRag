using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Data;
using SkinRag.Api.Models;
using SkinRag.Api.Services.Catalog;

namespace SkinRag.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(CatalogService catalog, IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
{
    [HttpGet("products")]
    public async Task<ActionResult<CatalogPage>> Products([FromQuery] CatalogSearchRequest request, CancellationToken ct) => Ok(await catalog.SearchAsync(request, ct));

    [HttpGet("products/{id:int}")]
    public async Task<ActionResult<ProductDto>> Product(int id, CancellationToken ct)
    {
        var product = await catalog.GetAsync(id, ct);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("filters")]
    public async Task<IActionResult> Filters(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(new
        {
            currency = "IRR",
            priceUnit = "ریال",
            domains = await db.Categories.AsNoTracking().Select(x => x.Domain).Distinct().OrderBy(x => x).ToListAsync(ct),
            categories = await db.Categories.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Slug, x.Name, x.Domain, x.ParentId }).ToListAsync(ct),
            brands = await db.Brands.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            profiles = await db.Profiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            concerns = await db.Concerns.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            ingredients = await db.Ingredients.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            shades = await db.ProductVariants.Where(x => x.IsActive && x.Shade != null).Select(x => x.Shade).Distinct().OrderBy(x => x).ToListAsync(ct),
            finishes = await db.ProductVariants.Where(x => x.IsActive && x.Finish != null).Select(x => x.Finish).Distinct().OrderBy(x => x).ToListAsync(ct)
        });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(new
        {
            totalProducts = await db.Products.CountAsync(ct),
            activeProducts = await db.Products.CountAsync(p => p.IsActive, ct),
            availableProducts = await CatalogService.Filter(db.Products, new CatalogFilters()).CountAsync(ct),
            variants = await db.ProductVariants.CountAsync(ct),
            categories = await db.Categories.CountAsync(ct),
            brands = await db.Brands.CountAsync(ct),
            domains = await db.Products.Where(p => p.CategoryDetails != null).GroupBy(p => p.CategoryDetails!.Domain).Select(g => new { domain = g.Key, products = g.Count(), activeProducts = g.Count(p => p.IsActive) }).ToListAsync(ct)
        });
    }
}
