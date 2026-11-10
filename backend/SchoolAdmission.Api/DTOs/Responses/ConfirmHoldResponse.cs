namespace SchoolAdmission.Api.DTOs.Responses;

public sealed class ConfirmHoldResponse
{
    public required string HoldId { get; init; }

    public required string Status { get; init; }
}
