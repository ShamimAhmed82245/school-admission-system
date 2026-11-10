using SchoolAdmission.Api.DTOs.Responses;

namespace SchoolAdmission.Api.Services.Interfaces;

public interface IHoldService
{
    Task<CreateHoldResult> CreateAsync(
        string classId,
        string? studentId,
        CancellationToken cancellationToken);

    Task<HoldDetailsResponse?> GetByIdAsync(string holdId, CancellationToken cancellationToken);

    Task<ConfirmHoldResult> ConfirmAsync(
        string holdId,
        string? studentId,
        CancellationToken cancellationToken);

    Task<CancelHoldResult> CancelAsync(
        string holdId,
        string? studentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StudentHoldResponse>> GetMyHoldsAsync(
        string? studentId,
        CancellationToken cancellationToken);

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

public abstract record ConfirmHoldResult
{
    private ConfirmHoldResult()
    {
    }

    public sealed record Success(ConfirmHoldResponse Hold) : ConfirmHoldResult;

    public sealed record NotFound : ConfirmHoldResult;

    public sealed record Forbidden(string Message) : ConfirmHoldResult;

    public sealed record Conflict(string Message) : ConfirmHoldResult;
}

public abstract record CancelHoldResult
{
    private CancelHoldResult()
    {
    }

    public sealed record Success : CancelHoldResult;

    public sealed record NotFound : CancelHoldResult;

    public sealed record Forbidden(string Message) : CancelHoldResult;

    public sealed record Conflict(string Message) : CancelHoldResult;
}
