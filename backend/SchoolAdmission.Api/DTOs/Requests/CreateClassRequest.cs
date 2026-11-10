using System.ComponentModel.DataAnnotations;

namespace SchoolAdmission.Api.DTOs.Requests;

public sealed class CreateClassRequest : IValidatableObject
{
    public const int MaxNameLength = 200;

    public string? Name { get; set; }

    public int TotalSeats { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult("Name is required.", [nameof(Name)]);
        }
        else if (Name.Trim().Length > MaxNameLength)
        {
            yield return new ValidationResult(
                $"Name must be {MaxNameLength} characters or fewer.",
                [nameof(Name)]);
        }

        if (TotalSeats <= 0)
        {
            yield return new ValidationResult(
                "Total seats must be greater than zero.",
                [nameof(TotalSeats)]);
        }
    }
}
