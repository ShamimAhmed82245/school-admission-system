using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Entities;
using SchoolAdmission.Api.Enums;
using SchoolAdmission.Api.Services.Implementations;
using SchoolAdmission.Api.Services.Interfaces;
using Xunit;

namespace SchoolAdmission.Api.Tests;

[Collection("class-api")]
public sealed class HoldApiTests : IAsyncLifetime
{
    private const string StudentId = "20152691234567890";

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public HoldApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "Holds", "Classes" RESTART IDENTITY CASCADE;""");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateHold_WithAvailableSeat_Returns201_AndDecreasesAvailableSeatsByOne()
    {
        var classId = await CreateClassAsync("Class 6 - Morning Shift", 3);

        var response = await PostHoldAsync(classId, StudentId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            ["holdId", "classId", "studentId", "status", "expiresAt"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());

        var holdId = document.RootElement.GetProperty("holdId").GetString();
        Assert.NotNull(holdId);
        Assert.StartsWith("h_", holdId, StringComparison.Ordinal);
        Assert.Equal(classId, document.RootElement.GetProperty("classId").GetString());
        Assert.Equal(StudentId, document.RootElement.GetProperty("studentId").GetString());
        Assert.Equal("HELD", document.RootElement.GetProperty("status").GetString());

        var expiresAt = document.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, expiresAt.Offset);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedClass = await dbContext.Classes.SingleAsync(schoolClass => schoolClass.Id == classId);
        var storedHold = await dbContext.Holds.SingleAsync();

        Assert.Equal(2, storedClass.AvailableSeats);
        Assert.Equal(3, storedClass.TotalSeats);
        Assert.Equal(holdId, storedHold.Id);
        Assert.Equal(classId, storedHold.ClassId);
        Assert.Equal(StudentId, storedHold.StudentId);
        Assert.Equal(HoldStatus.HELD, storedHold.Status);
        Assert.Equal(600, (storedHold.ExpiresAt - storedHold.CreatedAt).TotalSeconds, 3);
        Assert.Equal(
            storedHold.ExpiresAt.ToUniversalTime().ToUnixTimeMilliseconds(),
            expiresAt.ToUniversalTime().ToUnixTimeMilliseconds());

        var classResponse = await _client.GetAsync($"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, classResponse.StatusCode);
        using var classDocument = await ReadJsonAsync(classResponse);
        Assert.Equal(1, classDocument.RootElement.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, classDocument.RootElement.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(2, classDocument.RootElement.GetProperty("availableSeats").GetInt32());
    }

    [Theory]
    [InlineData(null, "X-Student-Id header is required.")]
    [InlineData("", "X-Student-Id header is required.")]
    [InlineData("   ", "X-Student-Id header is required.")]
    [InlineData("123", "X-Student-Id must contain exactly 17 digits.")]
    [InlineData("2015269123456789", "X-Student-Id must contain exactly 17 digits.")]
    [InlineData("201526912345678901", "X-Student-Id must contain exactly 17 digits.")]
    [InlineData("2015269123456789A", "X-Student-Id must contain exactly 17 digits.")]
    [InlineData("2015269123456789 ", "X-Student-Id must contain exactly 17 digits.")]
    public async Task CreateHold_WithMissingOrInvalidStudentId_Returns400_AndDoesNotChangeSeats(
        string? studentId,
        string expectedError)
    {
        var classId = await CreateClassAsync("Class 6", 4);

        var response = await PostHoldAsync(classId, studentId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Bad Request", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(expectedError, document.RootElement.GetProperty("errors")[0].GetString());

        await AssertSeatStateAsync(classId, availableSeats: 4, holdCount: 0);
    }

    [Fact]
    public async Task CreateHold_WhenClassDoesNotExist_Returns404_AndDoesNotCreateHold()
    {
        var response = await PostHoldAsync("c_missing", StudentId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Class not found.", document.RootElement.GetProperty("errors")[0].GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await dbContext.Holds.CountAsync());
    }

    [Fact]
    public async Task CreateHold_WhenNoSeatsRemain_Returns409_AndDoesNotDecreaseAvailableSeats()
    {
        var classId = await CreateClassAsync("Class 6", 1);
        var first = await PostHoldAsync(classId, StudentId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var response = await PostHoldAsync(classId, "20152691234567891");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(409, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Conflict", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "No seats are available for this class.",
            document.RootElement.GetProperty("errors")[0].GetString());

        await AssertSeatStateAsync(classId, availableSeats: 0, holdCount: 1);
    }

    [Fact]
    public async Task CreateHold_WhenStudentAlreadyHasHeldApplication_Returns409_AndDoesNotTakeAnotherSeat()
    {
        var classId = await CreateClassAsync("Class 6", 5);
        Assert.Equal(HttpStatusCode.Created, (await PostHoldAsync(classId, StudentId)).StatusCode);

        var response = await PostHoldAsync(classId, StudentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            "The student already has an active or confirmed application for this class.",
            document.RootElement.GetProperty("errors")[0].GetString());

        await AssertSeatStateAsync(classId, availableSeats: 4, holdCount: 1);
    }

    [Fact]
    public async Task CreateHold_WhenStudentAlreadyHasConfirmedApplication_Returns409_AndDoesNotTakeAnotherSeat()
    {
        var classId = await CreateClassAsync("Class 6", 5);
        var created = await PostHoldAsync(classId, StudentId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDocument = await ReadJsonAsync(created);
        var holdId = createdDocument.RootElement.GetProperty("holdId").GetString()!;
        await SetStatusAsync(holdId, HoldStatus.CONFIRMED);

        var response = await PostHoldAsync(classId, StudentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            "The student already has an active or confirmed application for this class.",
            document.RootElement.GetProperty("errors")[0].GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await dbContext.Holds.SingleAsync();
        Assert.Equal(HoldStatus.CONFIRMED, stored.Status);
        Assert.Equal(4, (await dbContext.Classes.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task CreateHold_WhenPreviousHoldIsCancelledOrExpired_ReservesOneSeat()
    {
        var classId = await CreateClassAsync("Class 6", 4);
        var created = await PostHoldAsync(classId, StudentId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDocument = await ReadJsonAsync(created);
        var holdId = createdDocument.RootElement.GetProperty("holdId").GetString()!;
        await SetStatusAsync(holdId, HoldStatus.CANCELLED);
        await SetAvailableSeatsAsync(classId, 4);

        var response = await PostHoldAsync(classId, StudentId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertSeatStateAsync(classId, availableSeats: 3, holdCount: 2);
    }

    [Fact]
    public async Task CreateHold_ConcurrentRequests_CannotReserveMoreSeatsThanAvailable()
    {
        const int totalSeats = 5;
        const int requestCount = 30;
        var classId = await CreateClassAsync("Class 6", totalSeats);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, requestCount).Select(index =>
                PostHoldAsync(classId, index.ToString("D17"))));

        try
        {
            Assert.Equal(totalSeats, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(
                requestCount - totalSeats,
                responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            Assert.DoesNotContain(responses, response =>
                response.StatusCode != HttpStatusCode.Created
                && response.StatusCode != HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await AssertSeatStateAsync(classId, availableSeats: 0, holdCount: totalSeats);
    }

    [Fact]
    public async Task CreateHold_ConcurrentDuplicateRequests_ReserveOnlyOneSeat()
    {
        var classId = await CreateClassAsync("Class 6", 8);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => PostHoldAsync(classId, StudentId)));

        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(11, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await AssertSeatStateAsync(classId, availableSeats: 7, holdCount: 1);
    }

    [Fact]
    public async Task GetHold_WhenHoldExists_Returns200WithCurrentStatus()
    {
        var classId = await CreateClassAsync("Class 6", 2);
        var created = await PostHoldAsync(classId, StudentId);
        created.EnsureSuccessStatusCode();
        using var createdDocument = await ReadJsonAsync(created);
        var holdId = createdDocument.RootElement.GetProperty("holdId").GetString()!;
        var expiresAt = createdDocument.RootElement.GetProperty("expiresAt").GetString();

        var response = await _client.GetAsync($"/holds/{holdId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            ["holdId", "classId", "status", "expiresAt"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(holdId, document.RootElement.GetProperty("holdId").GetString());
        Assert.Equal(classId, document.RootElement.GetProperty("classId").GetString());
        Assert.Equal("HELD", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(expiresAt, document.RootElement.GetProperty("expiresAt").GetString());
        await AssertSeatStateAsync(classId, availableSeats: 1, holdCount: 1);
    }

    [Fact]
    public async Task GetHold_WhenHoldDoesNotExist_Returns404()
    {
        var response = await _client.GetAsync("/holds/h_missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Hold not found.", document.RootElement.GetProperty("errors")[0].GetString());
    }

    [Theory]
    [InlineData(HoldStatus.CONFIRMED)]
    [InlineData(HoldStatus.CANCELLED)]
    [InlineData(HoldStatus.EXPIRED)]
    public async Task GetHold_DoesNotReleaseSeatForTerminalOrConfirmedStatus(HoldStatus status)
    {
        var classId = await CreateClassAsync("Class 6", 3);
        var holdId = await InsertHoldAsync(
            classId,
            StudentId,
            status,
            DateTimeOffset.UtcNow.AddMinutes(-5));
        await SetAvailableSeatsAsync(classId, status == HoldStatus.CONFIRMED ? 2 : 3);

        var first = await _client.GetAsync($"/holds/{holdId}");
        var second = await _client.GetAsync($"/holds/{holdId}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var firstDocument = await ReadJsonAsync(first);
        using var secondDocument = await ReadJsonAsync(second);
        Assert.Equal(status.ToString(), firstDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(status.ToString(), secondDocument.RootElement.GetProperty("status").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(status, (await dbContext.Holds.SingleAsync()).Status);
        Assert.Equal(
            status == HoldStatus.CONFIRMED ? 2 : 3,
            (await dbContext.Classes.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task GetHold_WhenHeldRecordIsDue_ExpiresItAndReleasesTheSeatOnlyOnce()
    {
        var classId = await CreateClassAsync("Class 6", 4);
        var holdId = await InsertHoldAsync(
            classId,
            StudentId,
            HoldStatus.HELD,
            DateTimeOffset.UtcNow.AddSeconds(-1));
        await SetAvailableSeatsAsync(classId, 3);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _client.GetAsync($"/holds/{holdId}")));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            foreach (var response in responses)
            {
                using var document = await ReadJsonAsync(response);
                Assert.Equal(
                    ["holdId", "classId", "status", "expiresAt"],
                    document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
                Assert.Equal("EXPIRED", document.RootElement.GetProperty("status").GetString());
                Assert.Equal(holdId, document.RootElement.GetProperty("holdId").GetString());
                Assert.Equal(classId, document.RootElement.GetProperty("classId").GetString());
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(HoldStatus.EXPIRED, (await dbContext.Holds.SingleAsync()).Status);
        Assert.Equal(4, (await dbContext.Classes.SingleAsync()).AvailableSeats);

        var repeat = await _client.GetAsync($"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        using var repeatDocument = await ReadJsonAsync(repeat);
        Assert.Equal("EXPIRED", repeatDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(4, (await dbContext.Classes.AsNoTracking().SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task ExpireDueHolds_ReleasesEachDueHoldOnceWithoutAnHttpCall()
    {
        var classId = await CreateClassAsync("Class 6", 5);
        await InsertHoldAsync(classId, "10000000000000001", HoldStatus.HELD, DateTimeOffset.UtcNow.AddMinutes(-2));
        await InsertHoldAsync(classId, "10000000000000002", HoldStatus.HELD, DateTimeOffset.UtcNow.AddMinutes(10));
        await InsertHoldAsync(classId, "10000000000000003", HoldStatus.CONFIRMED, DateTimeOffset.UtcNow.AddMinutes(-2));
        await SetAvailableSeatsAsync(classId, 2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var holdService = scope.ServiceProvider.GetRequiredService<IHoldService>();

        Assert.Equal(1, await holdService.ExpireDueHoldsAsync(CancellationToken.None));
        Assert.Equal(0, await holdService.ExpireDueHoldsAsync(CancellationToken.None));

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var holds = await dbContext.Holds.AsNoTracking().ToListAsync();
        Assert.Equal(HoldStatus.EXPIRED, holds.Single(hold => hold.StudentId == "10000000000000001").Status);
        Assert.Equal(HoldStatus.HELD, holds.Single(hold => hold.StudentId == "10000000000000002").Status);
        Assert.Equal(HoldStatus.CONFIRMED, holds.Single(hold => hold.StudentId == "10000000000000003").Status);
        Assert.Equal(3, (await dbContext.Classes.AsNoTracking().SingleAsync()).AvailableSeats);
        Assert.Equal(HoldService.DefaultHoldDurationSeconds, 600);
    }

    private async Task<string> CreateClassAsync(string name, int totalSeats)
    {
        var response = await _client.PostAsJsonAsync("/classes", new
        {
            name,
            totalSeats
        });
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response);
        return document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Created class did not include an id.");
    }

    private Task<HttpResponseMessage> PostHoldAsync(string classId, string? studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        if (studentId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Student-Id", studentId);
        }

        return _client.SendAsync(request);
    }

    private async Task<string> InsertHoldAsync(
        string classId,
        string studentId,
        HoldStatus status,
        DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var hold = new Hold
        {
            Id = $"h_{Guid.NewGuid():N}",
            StudentId = studentId,
            ClassId = classId,
            Status = status,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Holds.Add(hold);
        await dbContext.SaveChangesAsync();
        return hold.Id;
    }

    private async Task SetStatusAsync(string holdId, HoldStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hold = await dbContext.Holds.SingleAsync(candidate => candidate.Id == holdId);
        hold.Status = status;
        hold.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private async Task SetAvailableSeatsAsync(string classId, int availableSeats)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schoolClass = await dbContext.Classes.SingleAsync(candidate => candidate.Id == classId);
        schoolClass.AvailableSeats = availableSeats;
        schoolClass.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private async Task AssertSeatStateAsync(string classId, int availableSeats, int holdCount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schoolClass = await dbContext.Classes.SingleAsync(candidate => candidate.Id == classId);
        Assert.Equal(availableSeats, schoolClass.AvailableSeats);
        Assert.Equal(holdCount, await dbContext.Holds.CountAsync(hold => hold.ClassId == classId));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }
}
