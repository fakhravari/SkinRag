using Microsoft.AspNetCore.Mvc;

namespace SkinRag.Api.Presentation.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PagesController : Controller
{
    [HttpGet("/chat")]
    public IActionResult Chat()
    {
        return View();
    }

    [HttpGet("/admin")]
    public IActionResult Admin()
    {
        return View();
    }
}
