using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Services.Interfaces;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Route("holds")]
public sealed class HoldsController : ControllerBase
{
    private readonly IHoldService _holdService;

    public HoldsController(IHoldService holdService)
    {
        _holdService = holdService;
    }

    [HttpPost("/classes/{classId}/holds")]
    [ProducesResponseType(typeof(CreateHoldResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(string classId, CancellationToken cancellationToken)
    {
        var studentId = Request.Headers.TryGetValue("X-Student-Id", out var headerValues)
            ? headerValues.FirstOrDefault()
            : null;

        var result = await _holdService.CreateAsync(classId, studentId, cancellationToken);
        return result switch
        {
            CreateHoldResult.Success success => Created($"/holds/{success.Hold.HoldId}", success.Hold),
            CreateHoldResult.ClassNotFound => NotFound(new ErrorResponse
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Errors = ["Class not found."]
            }),
            CreateHoldResult.Conflict conflict => Conflict(new ErrorResponse
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Errors = [conflict.Message]
            }),
            _ => throw new InvalidOperationException("Unexpected hold creation result.")
        };
    }

    [HttpGet("{holdId}")]
    [ProducesResponseType(typeof(HoldDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string holdId, CancellationToken cancellationToken)
    {
        var hold = await _holdService.GetByIdAsync(holdId, cancellationToken);
        if (hold is null)
        {
            return NotFound(new ErrorResponse
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Errors = ["Hold not found."]
            });
        }

        return Ok(hold);
    }
}
