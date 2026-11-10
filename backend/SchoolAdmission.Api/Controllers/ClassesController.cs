using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.DTOs.Requests;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Services.Interfaces;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Route("classes")]
public sealed class ClassesController : ControllerBase
{
    private readonly IClassService _classService;

    public ClassesController(IClassService classService)
    {
        _classService = classService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateClassResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateClassRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _classService.CreateAsync(request, cancellationToken);
        return Created($"/classes/{created.Id}", created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ClassResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var classes = await _classService.GetAllAsync(cancellationToken);
        return Ok(classes);
    }

    [HttpGet("{classId}")]
    [ProducesResponseType(typeof(ClassResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string classId, CancellationToken cancellationToken)
    {
        var schoolClass = await _classService.GetByIdAsync(classId, cancellationToken);
        if (schoolClass is null)
        {
            return NotFound(new ErrorResponse
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Errors = ["Class not found."]
            });
        }

        return Ok(schoolClass);
    }
}
