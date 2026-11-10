namespace SchoolAdmission.Api.DTOs.Responses;

public sealed class ClassResponse
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required int TotalSeats { get; init; }

    public required int HeldSeats { get; init; }

    public required int ConfirmedSeats { get; init; }

    public required int AvailableSeats { get; init; }
}
