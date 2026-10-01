using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SkinRag.Api.Services;

namespace SkinRag.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController(KnowledgeIndexService indexService) : ControllerBase
{
    [HttpPost("rebuild")]
    [EnableRateLimiting("maintenance")]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken, [FromQuery] bool force = false)
    {
        if (RebuildMode() != "local")
        {
            return Problem(statusCode: 403, title: "بازسازی دستی فقط از همین دستگاه و با آدرس محلی در دسترس است.");
        }

        var origin = Request.Headers.Origin.ToString();
        if (Request.Headers["X-Requested-With"] != "XMLHttpRequest" || !Request.HasJsonContentType()
            || origin != $"{Request.Scheme}://{Request.Host}")
        {
            return Unauthorized();
        }

        await indexService.RebuildAsync(cancellationToken, force);
        return Ok(indexService.Status() with
        {
            ManualRebuildMode = RebuildMode()
        });
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(indexService.Status() with
        {
            ManualRebuildMode = RebuildMode()
        });
    }

    private string RebuildMode()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        var host = Request.Host.Host;
        var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || System.Net.IPAddress.TryParse(host, out var hostIp) && System.Net.IPAddress.IsLoopback(hostIp);
        return remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp) && localHost
            ? "local"
            : "disabled";
    }
}
