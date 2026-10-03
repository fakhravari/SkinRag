using Microsoft.AspNetCore.Mvc;

namespace SkinRag.Api.Presentation.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PagesController : Controller
{
    [HttpGet("/chat")]
    public IActionResult Chat() => View();
    [HttpGet("/admin")]
    public IActionResult Admin() => View();
}
