using System.Text.Json.Serialization;
using SchoolAdmission.Api.Serialization;

namespace SchoolAdmission.Api.DTOs.Responses;

public sealed class HoldDetailsResponse
{
    public required string HoldId { get; init; }

    public required string ClassId { get; init; }

    public required string Status { get; init; }

    [JsonConverter(typeof(UtcDateTimeOffsetJsonConverter))]
    public required DateTimeOffset ExpiresAt { get; init; }
}
