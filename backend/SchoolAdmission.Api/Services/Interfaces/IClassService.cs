using SchoolAdmission.Api.DTOs.Requests;
using SchoolAdmission.Api.DTOs.Responses;

namespace SchoolAdmission.Api.Services.Interfaces;

public interface IClassService
{
    Task<CreateClassResponse> CreateAsync(CreateClassRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClassResponse>> GetAllAsync(CancellationToken cancellationToken);

    Task<ClassResponse?> GetByIdAsync(string classId, CancellationToken cancellationToken);
}
