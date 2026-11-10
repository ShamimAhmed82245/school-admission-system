using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Requests;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Entities;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Services.Interfaces;

namespace SchoolAdmission.Api.Services.Implementations;

public sealed class ClassService : IClassService
{
    private readonly ApplicationDbContext _dbContext;

    public ClassService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreateClassResponse> CreateAsync(
        CreateClassRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ClassValidationException("Name is required.");
        }

        if (name.Length > CreateClassRequest.MaxNameLength)
        {
            throw new ClassValidationException(
                $"Name must be {CreateClassRequest.MaxNameLength} characters or fewer.");
        }

        if (request.TotalSeats <= 0)
        {
            throw new ClassValidationException("Total seats must be greater than zero.");
        }

        var now = DateTimeOffset.UtcNow;
        var schoolClass = new Class
        {
            Id = $"c_{Guid.NewGuid():N}",
            Name = name,
            TotalSeats = request.TotalSeats,
            AvailableSeats = request.TotalSeats,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.Classes.Add(schoolClass);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreateClassResponse
        {
            Id = schoolClass.Id,
            Name = schoolClass.Name,
            TotalSeats = schoolClass.TotalSeats
        };
    }

    public async Task<IReadOnlyList<ClassResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var classes = await _dbContext.Classes
            .AsNoTracking()
            .OrderBy(schoolClass => schoolClass.CreatedAt)
            .ThenBy(schoolClass => schoolClass.Id)
            .Select(schoolClass => new
            {
                schoolClass.Id,
                schoolClass.Name,
                schoolClass.TotalSeats,
                schoolClass.AvailableSeats
            })
            .ToListAsync(cancellationToken);

        var counts = await LoadSeatCountsAsync(classId: null, cancellationToken);
        var countsByClass = counts
            .GroupBy(row => row.ClassId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return classes
            .Select(schoolClass =>
            {
                countsByClass.TryGetValue(schoolClass.Id, out var classCounts);
                var (heldSeats, confirmedSeats) = SumSeatCounts(classCounts);
                return new ClassResponse
                {
                    Id = schoolClass.Id,
                    Name = schoolClass.Name,
                    TotalSeats = schoolClass.TotalSeats,
                    HeldSeats = heldSeats,
                    ConfirmedSeats = confirmedSeats,
                    AvailableSeats = schoolClass.AvailableSeats
                };
            })
            .ToList();
    }

    public async Task<ClassResponse?> GetByIdAsync(string classId, CancellationToken cancellationToken)
    {
        var schoolClass = await _dbContext.Classes
            .AsNoTracking()
            .Where(candidate => candidate.Id == classId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.TotalSeats,
                candidate.AvailableSeats
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (schoolClass is null)
        {
            return null;
        }

        var counts = await LoadSeatCountsAsync(schoolClass.Id, cancellationToken);
        var (heldSeats, confirmedSeats) = SumSeatCounts(counts);

        return new ClassResponse
        {
            Id = schoolClass.Id,
            Name = schoolClass.Name,
            TotalSeats = schoolClass.TotalSeats,
            HeldSeats = heldSeats,
            ConfirmedSeats = confirmedSeats,
            AvailableSeats = schoolClass.AvailableSeats
        };
    }

    private async Task<List<SeatCountRow>> LoadSeatCountsAsync(
        string? classId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTimeOffset.UtcNow;

        var query = _dbContext.Holds
            .AsNoTracking()
            .Where(hold =>
                hold.Status == HoldStatus.CONFIRMED
                || (hold.Status == HoldStatus.HELD && hold.ExpiresAt > utcNow));

        if (classId is not null)
        {
            query = query.Where(hold => hold.ClassId == classId);
        }

        return await query
            .GroupBy(hold => new { hold.ClassId, hold.Status })
            .Select(group => new SeatCountRow(
                group.Key.ClassId,
                group.Key.Status,
                group.Count()))
            .ToListAsync(cancellationToken);
    }

    private static (int HeldSeats, int ConfirmedSeats) SumSeatCounts(IEnumerable<SeatCountRow>? rows)
    {
        if (rows is null)
        {
            return (0, 0);
        }

        var heldSeats = 0;
        var confirmedSeats = 0;
        foreach (var row in rows)
        {
            if (row.Status == HoldStatus.HELD)
            {
                heldSeats += row.Count;
            }
            else if (row.Status == HoldStatus.CONFIRMED)
            {
                confirmedSeats += row.Count;
            }
        }

        return (heldSeats, confirmedSeats);
    }

    private sealed record SeatCountRow(string ClassId, HoldStatus Status, int Count);
}

public sealed class ClassValidationException : Exception
{
    public ClassValidationException(string message)
        : base(message)
    {
    }
}
