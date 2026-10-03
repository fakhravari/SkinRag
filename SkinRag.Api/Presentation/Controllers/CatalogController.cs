using Microsoft.AspNetCore.Mvc;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Contracts.Catalog;

namespace SkinRag.Api.Presentation.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(ICatalogQueryService catalog) : ControllerBase
{
    [HttpGet("products")]
    public async Task<ActionResult<CatalogPage>> Products([FromQuery] CatalogSearchRequest request,
        CancellationToken ct)
    {
        return Ok(await catalog.SearchAsync(request, ct));
    }

    [HttpGet("products/{id:int}")]
    public async Task<ActionResult<ProductDto>> Product(int id, CancellationToken ct)
    {
        var product = await catalog.GetAsync(id, ct);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("filters")]
    public async Task<ActionResult<CatalogFilterOptions>> Filters(CancellationToken ct)
    {
        return Ok(await catalog.GetFiltersAsync(ct));
    }

    [HttpGet("stats")]
    public async Task<ActionResult<CatalogStatistics>> Stats(CancellationToken ct)
    {
        return Ok(await catalog.GetStatisticsAsync(ct));
    }
}
