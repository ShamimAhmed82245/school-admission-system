namespace SchoolAdmission.Api.Entities;

public class Class
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int TotalSeats { get; set; }

    public int AvailableSeats { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Hold> Holds { get; set; } = new List<Hold>();
}
