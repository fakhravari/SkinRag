using Microsoft.AspNetCore.Mvc;
using SkinRag.Api.Services.Knowledge;

namespace SkinRag.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController(KnowledgeIndexService indexService) : ControllerBase
{
    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken, [FromQuery] bool force = false)
    {
        await indexService.RebuildAsync(cancellationToken, force);
        return Ok(indexService.Status());
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(indexService.Status());
    }
}
