using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Entities;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Services.Interfaces;

namespace SchoolAdmission.Api.Services.Implementations;

public sealed class HoldService : IHoldService
{
    public const int DefaultHoldDurationSeconds = 600;

    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public HoldService(ApplicationDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task<CreateHoldResult> CreateAsync(
        string classId,
        string? studentId,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);

        var classExists = await _dbContext.Classes
            .AsNoTracking()
            .AnyAsync(schoolClass => schoolClass.Id == classId, cancellationToken);
        if (!classExists)
        {
            return new CreateHoldResult.ClassNotFound();
        }

        var now = TruncateToUtcMicroseconds(DateTimeOffset.UtcNow);
        await ExpireDueHoldsForClassAsync(classId, now, cancellationToken);

        var durationSeconds = ResolveHoldDurationSeconds();
        var expiresAt = TruncateToUtcMicroseconds(now.AddSeconds(durationSeconds));
        var hold = new Hold
        {
            Id = $"h_{Guid.NewGuid():N}",
            StudentId = studentId,
            ClassId = classId,
            Status = HoldStatus.HELD,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await LockClassAsync(classId, cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CreateHoldResult.ClassNotFound();
            }

            if (await HasActiveOrConfirmedApplicationAsync(classId, studentId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CreateHoldResult.Conflict(
                    "The student already has an active or confirmed application for this class.");
            }

            // Conditional update: only one concurrent transaction can take each remaining seat.
            var reserved = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Classes"
                SET "AvailableSeats" = "AvailableSeats" - 1,
                    "UpdatedAt" = {now}
                WHERE "Id" = {classId}
                  AND "AvailableSeats" > 0
                """,
                cancellationToken);

            if (reserved != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CreateHoldResult.Conflict("No seats are available for this class.");
            }

            _dbContext.Holds.Add(hold);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsActiveApplicationConflict(exception))
            {
                _dbContext.Entry(hold).State = EntityState.Detached;
                await transaction.RollbackAsync(cancellationToken);
                return new CreateHoldResult.Conflict(
                    "The student already has an active or confirmed application for this class.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            throw;
        }

        return new CreateHoldResult.Success(new CreateHoldResponse
        {
            HoldId = hold.Id,
            ClassId = hold.ClassId,
            StudentId = hold.StudentId,
            Status = hold.Status.ToString(),
            ExpiresAt = hold.ExpiresAt
        });
    }

    public async Task<HoldDetailsResponse?> GetByIdAsync(
        string holdId,
        CancellationToken cancellationToken)
    {
        var classId = await _dbContext.Holds
            .AsNoTracking()
            .Where(hold => hold.Id == holdId)
            .Select(hold => hold.ClassId)
            .FirstOrDefaultAsync(cancellationToken);

        if (classId is null)
        {
            return null;
        }

        await ExpireDueHoldsForClassAsync(
            classId,
            TruncateToUtcMicroseconds(DateTimeOffset.UtcNow),
            cancellationToken);

        var hold = await _dbContext.Holds
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == holdId, cancellationToken);

        return ToDetails(hold);
    }

    public async Task<ConfirmHoldResult> ConfirmAsync(
        string holdId,
        string? studentId,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);
        var now = TruncateToUtcMicroseconds(DateTimeOffset.UtcNow);

        var classId = await _dbContext.Holds
            .AsNoTracking()
            .Where(hold => hold.Id == holdId)
            .Select(hold => hold.ClassId)
            .FirstOrDefaultAsync(cancellationToken);
        if (classId is null)
        {
            return new ConfirmHoldResult.NotFound();
        }

        await ExpireDueHoldsForClassAsync(classId, now, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await LockClassAsync(classId, cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult.NotFound();
            }

            var hold = await _dbContext.Holds
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == holdId, cancellationToken);
            if (hold is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult.NotFound();
            }

            if (!string.Equals(hold.StudentId, studentId, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult.Forbidden(
                    "Only the student who created this hold can confirm it.");
            }

            if (hold.Status == HoldStatus.CONFIRMED)
            {
                await transaction.CommitAsync(cancellationToken);
                return Confirmed(hold.Id);
            }

            if (hold.Status is HoldStatus.EXPIRED or HoldStatus.CANCELLED)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult.Conflict(
                    hold.Status == HoldStatus.EXPIRED
                        ? "The hold has expired and cannot be confirmed."
                        : "The hold has been cancelled and cannot be confirmed.");
            }

            if (hold.Status != HoldStatus.HELD || hold.ExpiresAt <= now)
            {
                await ExpireDueHoldsInCurrentTransactionAsync(classId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new ConfirmHoldResult.Conflict(
                    "The hold has expired and cannot be confirmed.");
            }

            // Confirm only while still HELD and unexpired; AvailableSeats is unchanged.
            var confirmed = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Holds"
                SET "Status" = {nameof(HoldStatus.CONFIRMED)},
                    "UpdatedAt" = {now}
                WHERE "Id" = {holdId}
                  AND "StudentId" = {studentId}
                  AND "Status" = {nameof(HoldStatus.HELD)}
                  AND "ExpiresAt" > {now}
                """,
                cancellationToken);

            if (confirmed != 1)
            {
                await ExpireDueHoldsInCurrentTransactionAsync(classId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var current = await _dbContext.Holds
                    .AsNoTracking()
                    .FirstOrDefaultAsync(candidate => candidate.Id == holdId, cancellationToken);
                if (current is null)
                {
                    return new ConfirmHoldResult.NotFound();
                }

                if (current.Status == HoldStatus.CONFIRMED
                    && string.Equals(current.StudentId, studentId, StringComparison.Ordinal))
                {
                    return Confirmed(current.Id);
                }

                return new ConfirmHoldResult.Conflict(
                    current.Status == HoldStatus.CANCELLED
                        ? "The hold has been cancelled and cannot be confirmed."
                        : "The hold has expired and cannot be confirmed.");
            }

            await transaction.CommitAsync(cancellationToken);
            return Confirmed(holdId);
        }
        catch
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<CancelHoldResult> CancelAsync(
        string holdId,
        string? studentId,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);
        var now = TruncateToUtcMicroseconds(DateTimeOffset.UtcNow);

        var classId = await _dbContext.Holds
            .AsNoTracking()
            .Where(hold => hold.Id == holdId)
            .Select(hold => hold.ClassId)
            .FirstOrDefaultAsync(cancellationToken);
        if (classId is null)
        {
            return new CancelHoldResult.NotFound();
        }

        await ExpireDueHoldsForClassAsync(classId, now, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await LockClassAsync(classId, cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult.NotFound();
            }

            var hold = await _dbContext.Holds
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == holdId, cancellationToken);
            if (hold is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult.NotFound();
            }

            if (!string.Equals(hold.StudentId, studentId, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult.Forbidden(
                    "Only the student who created this hold can cancel it.");
            }

            if (hold.Status == HoldStatus.CANCELLED)
            {
                await transaction.CommitAsync(cancellationToken);
                return new CancelHoldResult.Success();
            }

            if (hold.Status == HoldStatus.CONFIRMED)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult.Conflict(
                    "A confirmed application cannot be cancelled.");
            }

            if (hold.Status == HoldStatus.EXPIRED)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult.Conflict(
                    "The hold has expired and cannot be cancelled.");
            }

            // Transition HELD → CANCELLED and release the seat in the same transaction.
            var cancelled = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Holds"
                SET "Status" = {nameof(HoldStatus.CANCELLED)},
                    "UpdatedAt" = {now}
                WHERE "Id" = {holdId}
                  AND "StudentId" = {studentId}
                  AND "Status" = {nameof(HoldStatus.HELD)}
                  AND "ExpiresAt" > {now}
                """,
                cancellationToken);

            if (cancelled != 1)
            {
                await ExpireDueHoldsInCurrentTransactionAsync(classId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var current = await _dbContext.Holds
                    .AsNoTracking()
                    .FirstOrDefaultAsync(candidate => candidate.Id == holdId, cancellationToken);
                if (current is null)
                {
                    return new CancelHoldResult.NotFound();
                }

                if (current.Status == HoldStatus.CANCELLED
                    && string.Equals(current.StudentId, studentId, StringComparison.Ordinal))
                {
                    return new CancelHoldResult.Success();
                }

                return new CancelHoldResult.Conflict(
                    current.Status == HoldStatus.CONFIRMED
                        ? "A confirmed application cannot be cancelled."
                        : "The hold has expired and cannot be cancelled.");
            }

            var released = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Classes"
                SET "AvailableSeats" = "AvailableSeats" + 1,
                    "UpdatedAt" = {now}
                WHERE "Id" = {classId}
                  AND "AvailableSeats" + 1 <= "TotalSeats"
                """,
                cancellationToken);

            if (released != 1)
            {
                throw new InvalidOperationException(
                    "Cancelled hold could not release its seat without exceeding total seats.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new CancelHoldResult.Success();
        }
        catch
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<StudentHoldResponse>> GetMyHoldsAsync(
        string? studentId,
        CancellationToken cancellationToken)
    {
        studentId = ValidateStudentId(studentId);
        var now = TruncateToUtcMicroseconds(DateTimeOffset.UtcNow);

        var classIds = await _dbContext.Holds
            .AsNoTracking()
            .Where(hold =>
                hold.StudentId == studentId
                && hold.Status == HoldStatus.HELD
                && hold.ExpiresAt <= now)
            .Select(hold => hold.ClassId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var classId in classIds)
        {
            await ExpireDueHoldsForClassAsync(classId, now, cancellationToken);
        }

        var rows = await _dbContext.Holds
            .AsNoTracking()
            .Where(hold => hold.StudentId == studentId)
            .Join(
                _dbContext.Classes.AsNoTracking(),
                hold => hold.ClassId,
                schoolClass => schoolClass.Id,
                (hold, schoolClass) => new
                {
                    hold.Id,
                    hold.ClassId,
                    schoolClass.Name,
                    hold.Status,
                    hold.ExpiresAt
                })
            .OrderBy(hold => hold.ExpiresAt)
            .ThenBy(hold => hold.Id)
            .ToListAsync(cancellationToken);

        return rows
            .Select(hold => new StudentHoldResponse
            {
                HoldId = hold.Id,
                ClassId = hold.ClassId,
                ClassName = hold.Name,
                Status = hold.Status.ToString(),
                ExpiresAt = hold.ExpiresAt
            })
            .ToList();
    }

    public async Task<int> ExpireDueHoldsAsync(CancellationToken cancellationToken)
    {
        var now = TruncateToUtcMicroseconds(DateTimeOffset.UtcNow);
        var classIds = await _dbContext.Holds
            .AsNoTracking()
            .Where(hold => hold.Status == HoldStatus.HELD && hold.ExpiresAt <= now)
            .Select(hold => hold.ClassId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var classId in classIds)
        {
            expired += await ExpireDueHoldsForClassAsync(classId, now, cancellationToken);
        }

        return expired;
    }

    private async Task<int> ExpireDueHoldsForClassAsync(
        string classId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await LockClassAsync(classId, cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return 0;
            }

            var expiredCount = await ExpireDueHoldsInCurrentTransactionAsync(
                classId,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return expiredCount;
        }
        catch
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task<int> ExpireDueHoldsInCurrentTransactionAsync(
        string classId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiredCount = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Holds"
            SET "Status" = {nameof(HoldStatus.EXPIRED)},
                "UpdatedAt" = {now}
            WHERE "ClassId" = {classId}
              AND "Status" = {nameof(HoldStatus.HELD)}
              AND "ExpiresAt" <= {now}
            """,
            cancellationToken);

        if (expiredCount > 0)
        {
            var released = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Classes"
                SET "AvailableSeats" = "AvailableSeats" + {expiredCount},
                    "UpdatedAt" = {now}
                WHERE "Id" = {classId}
                  AND "AvailableSeats" + {expiredCount} <= "TotalSeats"
                """,
                cancellationToken);

            if (released != 1)
            {
                throw new InvalidOperationException(
                    "Expired holds could not release their seats without exceeding total seats.");
            }
        }

        return expiredCount;
    }

    private static ConfirmHoldResult.Success Confirmed(string holdId)
    {
        return new ConfirmHoldResult.Success(new ConfirmHoldResponse
        {
            HoldId = holdId,
            Status = nameof(HoldStatus.CONFIRMED)
        });
    }

    private Task<int> LockClassAsync(string classId, CancellationToken cancellationToken)
    {
        return _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Classes"
            SET "UpdatedAt" = "UpdatedAt"
            WHERE "Id" = {classId}
            """,
            cancellationToken);
    }

    private Task<bool> HasActiveOrConfirmedApplicationAsync(
        string classId,
        string studentId,
        CancellationToken cancellationToken)
    {
        return _dbContext.Holds.AsNoTracking().AnyAsync(
            hold => hold.ClassId == classId
                && hold.StudentId == studentId
                && (hold.Status == HoldStatus.HELD || hold.Status == HoldStatus.CONFIRMED),
            cancellationToken);
    }

    private int ResolveHoldDurationSeconds()
    {
        var configured = _configuration["HOLD_DURATION_SECONDS"];
        if (int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            return seconds;
        }

        return DefaultHoldDurationSeconds;
    }

    private static string ValidateStudentId(string? studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new HoldValidationException("X-Student-Id header is required.");
        }

        if (studentId.Length != 17 || studentId.Any(character => character is < '0' or > '9'))
        {
            throw new HoldValidationException("X-Student-Id must contain exactly 17 digits.");
        }

        return studentId;
    }

    // PostgreSQL timestamptz stores microsecond precision; keep in-memory values aligned.
    private static DateTimeOffset TruncateToUtcMicroseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.UtcTicks - (utc.UtcTicks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static HoldDetailsResponse ToDetails(Hold hold)
    {
        return new HoldDetailsResponse
        {
            HoldId = hold.Id,
            ClassId = hold.ClassId,
            Status = hold.Status.ToString(),
            ExpiresAt = hold.ExpiresAt
        };
    }

    private static bool IsActiveApplicationConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && postgres.ConstraintName == "IX_Holds_StudentId_ClassId_ActiveOrConfirmed")
            {
                return true;
            }
        }

        return false;
    }

    private static async Task RollbackIfActiveAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The transaction was already committed or rolled back.
        }
    }
}

public sealed class HoldValidationException : Exception
{
    public HoldValidationException(string message)
        : base(message)
    {
    }
}
