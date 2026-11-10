namespace SchoolAdmission.Api.DTOs.Responses;

public sealed class CreateClassResponse
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required int TotalSeats { get; init; }
}
