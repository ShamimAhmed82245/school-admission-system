using SchoolAdmission.Api.DTOs.Responses;

namespace SchoolAdmission.Api.Services.Interfaces;

public interface IHoldService
{
    Task<CreateHoldResult> CreateAsync(
        string classId,
        string? studentId,
        CancellationToken cancellationToken);

    Task<HoldDetailsResponse?> GetByIdAsync(string holdId, CancellationToken cancellationToken);

    Task<int> ExpireDueHoldsAsync(CancellationToken cancellationToken);
}

public abstract record CreateHoldResult
{
    private CreateHoldResult()
    {
    }

    public sealed record Success(CreateHoldResponse Hold) : CreateHoldResult;

    public sealed record ClassNotFound : CreateHoldResult;

    public sealed record Conflict(string Message) : CreateHoldResult;
}
