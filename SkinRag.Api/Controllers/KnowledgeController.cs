using Microsoft.AspNetCore.Mvc;
using SkinRag.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography;
using System.Text;

namespace SkinRag.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController(KnowledgeIndexService indexService, IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("rebuild")]
    [EnableRateLimiting("maintenance")]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken, [FromQuery] bool force = false)
    {
        var expected = configuration["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            if (RebuildMode() != "local-development")
            {
                return Problem(statusCode: 503, title: "بازسازی دستی فعال نیست؛ کلید مدیریتی روی سرور تنظیم نشده است.");
            }

            var origin = Request.Headers.Origin.ToString();
            if (Request.Headers["X-Requested-With"] != "XMLHttpRequest" || !Request.HasJsonContentType()

                || (origin.Length > 0 && origin != $"{Request.Scheme}://{Request.Host}"))
            {
                return Unauthorized();
            }
        }
        else if (!CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                        SHA256.HashData(Encoding.UTF8.GetBytes(Request.Headers["X-Admin-Key"].ToString()))))
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
        if (!string.IsNullOrWhiteSpace(configuration["Admin:ApiKey"]))
        {
            return "api-key";
        }

        return environment.IsDevelopment() && HttpContext.Connection.RemoteIpAddress is { } ip

            && System.Net.IPAddress.IsLoopback(ip) ? "local-development" : "disabled";
    }
}
