using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Models;

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
        {
            return ValidationProblem(ModelState);
        }

        var result = await consultationService.AskAsync(request, cancellationToken);
        return Ok(result);
    }
}
