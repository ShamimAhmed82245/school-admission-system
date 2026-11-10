namespace SchoolAdmission.Api.DTOs.Responses;

public sealed class ErrorResponse
{
    public required int Status { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }
}
