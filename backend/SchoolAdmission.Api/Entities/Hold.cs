using SchoolAdmission.Api.Enums;

namespace SchoolAdmission.Api.Entities;

public class Hold
{
    public string Id { get; set; } = null!;

    public string StudentId { get; set; } = null!;

    public string ClassId { get; set; } = null!;

    public HoldStatus Status { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Class Class { get; set; } = null!;
}
