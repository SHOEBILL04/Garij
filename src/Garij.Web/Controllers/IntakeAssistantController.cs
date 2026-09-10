using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class IntakeAssistantController : Controller
{
    private readonly IIntelligenceService _intelligenceService;

    public IntakeAssistantController(IIntelligenceService intelligenceService)
    {
        _intelligenceService = intelligenceService;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suggest([FromBody] IntakeSuggestionRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ComplaintText))
        {
            return BadRequest(new IntakeSuggestionResponseDto
            {
                Success = false,
                ErrorMessage = "Please provide customer complaint or vehicle symptoms."
            });
        }

        var result = await _intelligenceService.SuggestServicesForIntakeAsync(request.ComplaintText, cancellationToken);
        return Json(result);
    }
}
