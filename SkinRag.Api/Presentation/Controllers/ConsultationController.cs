using Microsoft.AspNetCore.Mvc;
using SkinRag.Api.Application.Consultation;
using SkinRag.Api.Application.Contracts.Catalog;
using SkinRag.Api.Application.Contracts.Consultation;
using SkinRag.Api.Domain.Catalog;

namespace SkinRag.Api.Presentation.Controllers;

[ApiController]
[Route("api/consultation")]
public sealed class ConsultationController(ConsultationService consultationService) : ControllerBase
{
    [HttpPost("ask")]
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
