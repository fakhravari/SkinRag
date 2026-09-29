using Microsoft.AspNetCore.Mvc;
using SkinRag.Api.Models;
using SkinRag.Api.Application.Consultation;
using Microsoft.AspNetCore.RateLimiting;

namespace SkinRag.Api.Controllers;

[ApiController]
[Route("api/consultation")]
public sealed class ConsultationController(ConsultationService consultationService) : ControllerBase
{
    [HttpPost("ask")]
    [EnableRateLimiting("consultation")]
    [ProducesResponseType<ConsultationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConsultationResponse>> Ask([FromBody] ConsultationRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var result = await consultationService.AskAsync(request, cancellationToken);
        return Ok(result);
    }
}
