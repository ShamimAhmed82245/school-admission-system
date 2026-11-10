using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Entities;
using SchoolAdmission.Api.Enums;
using Xunit;

namespace SchoolAdmission.Api.Tests;

[Collection("class-api")]
public sealed class HoldLifecycleApiTests : IAsyncLifetime
{
    private const string StudentId = "20152691234567890";
    private const string OtherStudentId = "20152691234567891";

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public HoldLifecycleApiTests(ApiFactory factory)
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
    public async Task ConfirmHold_OwnedUnexpiredHeld_Returns200_AndDoesNotChangeAvailableSeats()
    {
        var classId = await CreateClassAsync("Class 6 - Morning Shift", 5);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var response = await ConfirmHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            ["holdId", "status"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(holdId, document.RootElement.GetProperty("holdId").GetString());
        Assert.Equal("CONFIRMED", document.RootElement.GetProperty("status").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hold = await dbContext.Holds.SingleAsync();
        var schoolClass = await dbContext.Classes.SingleAsync();
        Assert.Equal(HoldStatus.CONFIRMED, hold.Status);
        Assert.Equal(4, schoolClass.AvailableSeats);

        var classResponse = await _client.GetAsync($"/classes/{classId}");
        using var classDocument = await ReadJsonAsync(classResponse);
        Assert.Equal(0, classDocument.RootElement.GetProperty("heldSeats").GetInt32());
        Assert.Equal(1, classDocument.RootElement.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(4, classDocument.RootElement.GetProperty("availableSeats").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("2015269123456789A")]
    public async Task ConfirmHold_WithMissingOrInvalidStudentId_Returns400(string? studentId)
    {
        var classId = await CreateClassAsync("Class 6", 2);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var response = await ConfirmHoldAsync(holdId, studentId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertStatusAsync(holdId, HoldStatus.HELD);
        await AssertAvailableSeatsAsync(classId, 1);
    }

    [Fact]
    public async Task ConfirmHold_WhenHoldDoesNotExist_Returns404()
    {
        var response = await ConfirmHoldAsync("h_missing", StudentId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal("Hold not found.", document.RootElement.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task ConfirmHold_WhenOwnedByAnotherStudent_Returns403()
    {
        var classId = await CreateClassAsync("Class 6", 2);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var response = await ConfirmHoldAsync(holdId, OtherStudentId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStatusAsync(holdId, HoldStatus.HELD);
        await AssertAvailableSeatsAsync(classId, 1);
    }

    [Fact]
    public async Task ConfirmHold_WhenExpired_Returns409_AndReleasesSeatOnce()
    {
        var classId = await CreateClassAsync("Class 6", 3);
        var holdId = await InsertHoldAsync(
            classId,
            StudentId,
            HoldStatus.HELD,
            DateTimeOffset.UtcNow.AddSeconds(-2));
        await SetAvailableSeatsAsync(classId, 2);

        var response = await ConfirmHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            "The hold has expired and cannot be confirmed.",
            document.RootElement.GetProperty("errors")[0].GetString());
        await AssertStatusAsync(holdId, HoldStatus.EXPIRED);
        await AssertAvailableSeatsAsync(classId, 3);

        var repeat = await ConfirmHoldAsync(holdId, StudentId);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        await AssertAvailableSeatsAsync(classId, 3);
    }

    [Fact]
    public async Task ConfirmHold_WhenCancelled_Returns409()
    {
        var classId = await CreateClassAsync("Class 6", 2);
        var holdId = await CreateHoldAsync(classId, StudentId);
        await SetStatusAsync(holdId, HoldStatus.CANCELLED);
        await SetAvailableSeatsAsync(classId, 2);

        var response = await ConfirmHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            "The hold has been cancelled and cannot be confirmed.",
            document.RootElement.GetProperty("errors")[0].GetString());
        await AssertStatusAsync(holdId, HoldStatus.CANCELLED);
        await AssertAvailableSeatsAsync(classId, 2);
    }

    [Fact]
    public async Task ConfirmHold_RepeatedConfirmation_IsIdempotentWithoutChangingSeats()
    {
        var classId = await CreateClassAsync("Class 6", 4);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var first = await ConfirmHoldAsync(holdId, StudentId);
        var second = await ConfirmHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var firstDocument = await ReadJsonAsync(first);
        using var secondDocument = await ReadJsonAsync(second);
        Assert.Equal("CONFIRMED", firstDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal("CONFIRMED", secondDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(holdId, secondDocument.RootElement.GetProperty("holdId").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await dbContext.Holds.CountAsync());
        Assert.Equal(HoldStatus.CONFIRMED, (await dbContext.Holds.SingleAsync()).Status);
        Assert.Equal(3, (await dbContext.Classes.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task ConfirmHold_ConcurrentRequests_DoNotCorruptState()
    {
        var classId = await CreateClassAsync("Class 6", 3);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => ConfirmHoldAsync(holdId, StudentId)));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            foreach (var response in responses)
            {
                using var document = await ReadJsonAsync(response);
                Assert.Equal("CONFIRMED", document.RootElement.GetProperty("status").GetString());
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
        Assert.Equal(1, await dbContext.Holds.CountAsync());
        Assert.Equal(HoldStatus.CONFIRMED, (await dbContext.Holds.SingleAsync()).Status);
        Assert.Equal(2, (await dbContext.Classes.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task CancelHold_OwnedHeld_Returns204_AndReleasesExactlyOneSeat()
    {
        var classId = await CreateClassAsync("Class 6", 5);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var response = await CancelHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());

        await AssertStatusAsync(holdId, HoldStatus.CANCELLED);
        await AssertAvailableSeatsAsync(classId, 5);

        var classResponse = await _client.GetAsync($"/classes/{classId}");
        using var classDocument = await ReadJsonAsync(classResponse);
        Assert.Equal(0, classDocument.RootElement.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, classDocument.RootElement.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(5, classDocument.RootElement.GetProperty("availableSeats").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("201526912345678901")]
    public async Task CancelHold_WithMissingOrInvalidStudentId_Returns400(string? studentId)
    {
        var classId = await CreateClassAsync("Class 6", 2);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var response = await CancelHoldAsync(holdId, studentId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertStatusAsync(holdId, HoldStatus.HELD);
        await AssertAvailableSeatsAsync(classId, 1);
    }

    [Fact]
    public async Task CancelHold_WhenHoldDoesNotExist_Returns404()
    {
        var response = await CancelHoldAsync("h_missing", StudentId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelHold_WhenOwnedByAnotherStudent_Returns403()
    {
        var classId = await CreateClassAsync("Class 6", 2);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var response = await CancelHoldAsync(holdId, OtherStudentId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStatusAsync(holdId, HoldStatus.HELD);
        await AssertAvailableSeatsAsync(classId, 1);
    }

    [Fact]
    public async Task CancelHold_WhenConfirmed_Returns409_AndDoesNotReleaseSeat()
    {
        var classId = await CreateClassAsync("Class 6", 3);
        var holdId = await CreateHoldAsync(classId, StudentId);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmHoldAsync(holdId, StudentId)).StatusCode);

        var response = await CancelHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            "A confirmed application cannot be cancelled.",
            document.RootElement.GetProperty("errors")[0].GetString());
        await AssertStatusAsync(holdId, HoldStatus.CONFIRMED);
        await AssertAvailableSeatsAsync(classId, 2);
    }

    [Fact]
    public async Task CancelHold_WhenExpired_Returns409_AndDoesNotReleaseSeatAgain()
    {
        var classId = await CreateClassAsync("Class 6", 3);
        var holdId = await InsertHoldAsync(
            classId,
            StudentId,
            HoldStatus.HELD,
            DateTimeOffset.UtcNow.AddSeconds(-2));
        await SetAvailableSeatsAsync(classId, 2);

        var response = await CancelHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertStatusAsync(holdId, HoldStatus.EXPIRED);
        await AssertAvailableSeatsAsync(classId, 3);

        var repeat = await CancelHoldAsync(holdId, StudentId);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        await AssertAvailableSeatsAsync(classId, 3);
    }

    [Fact]
    public async Task CancelHold_RepeatedCancellation_IsIdempotentWithoutReleasingMultipleSeats()
    {
        var classId = await CreateClassAsync("Class 6", 4);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var first = await CancelHoldAsync(holdId, StudentId);
        var second = await CancelHoldAsync(holdId, StudentId);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        await AssertStatusAsync(holdId, HoldStatus.CANCELLED);
        await AssertAvailableSeatsAsync(classId, 4);
    }

    [Fact]
    public async Task CancelHold_ConcurrentCancellationAndExpiration_ReleaseSeatOnlyOnce()
    {
        var classId = await CreateClassAsync("Class 6", 2);
        var holdId = await InsertHoldAsync(
            classId,
            StudentId,
            HoldStatus.HELD,
            DateTimeOffset.UtcNow.AddMilliseconds(50));
        await SetAvailableSeatsAsync(classId, 1);

        await Task.Delay(80);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => CancelHoldAsync(holdId, StudentId)));

        try
        {
            Assert.DoesNotContain(
                responses,
                response => response.StatusCode
                    is not HttpStatusCode.NoContent
                    and not HttpStatusCode.Conflict);
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
        var hold = await dbContext.Holds.SingleAsync();
        Assert.True(hold.Status is HoldStatus.CANCELLED or HoldStatus.EXPIRED);
        Assert.Equal(2, (await dbContext.Classes.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task GetMyHolds_ReturnsAllApplicationsForStudentWithClassNames()
    {
        var firstClassId = await CreateClassAsync("Class 6 - Morning Shift", 5);
        var secondClassId = await CreateClassAsync("Class 7 - Day Shift", 5);
        var firstHoldId = await CreateHoldAsync(firstClassId, StudentId);
        var secondHoldId = await CreateHoldAsync(secondClassId, StudentId);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmHoldAsync(secondHoldId, StudentId)).StatusCode);
        await CreateHoldAsync(firstClassId, OtherStudentId);

        var response = await GetMyHoldsAsync(StudentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(2, document.RootElement.GetArrayLength());

        var items = document.RootElement.EnumerateArray().ToDictionary(
            item => item.GetProperty("holdId").GetString()!,
            item => item);

        Assert.True(items.ContainsKey(firstHoldId));
        Assert.True(items.ContainsKey(secondHoldId));
        Assert.Equal(
            ["holdId", "classId", "className", "status", "expiresAt"],
            items[firstHoldId].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(firstClassId, items[firstHoldId].GetProperty("classId").GetString());
        Assert.Equal("Class 6 - Morning Shift", items[firstHoldId].GetProperty("className").GetString());
        Assert.Equal("HELD", items[firstHoldId].GetProperty("status").GetString());
        Assert.Equal(secondClassId, items[secondHoldId].GetProperty("classId").GetString());
        Assert.Equal("Class 7 - Day Shift", items[secondHoldId].GetProperty("className").GetString());
        Assert.Equal("CONFIRMED", items[secondHoldId].GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetMyHolds_WhenStudentHasNoApplications_ReturnsEmptyArray()
    {
        var response = await GetMyHoldsAsync(StudentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public async Task GetMyHolds_WithMissingOrInvalidStudentId_Returns400(string? studentId)
    {
        var response = await GetMyHoldsAsync(studentId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMyHolds_DoesNotReturnOtherStudentsApplications()
    {
        var classId = await CreateClassAsync("Class 6", 4);
        await CreateHoldAsync(classId, OtherStudentId);

        var response = await GetMyHoldsAsync(StudentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetMyHolds_ReturnsExpiredStatusAfterProcessingDueHold()
    {
        var classId = await CreateClassAsync("Class 6", 3);
        var holdId = await InsertHoldAsync(
            classId,
            StudentId,
            HoldStatus.HELD,
            DateTimeOffset.UtcNow.AddSeconds(-1));
        await SetAvailableSeatsAsync(classId, 2);

        var response = await GetMyHoldsAsync(StudentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(1, document.RootElement.GetArrayLength());
        var item = document.RootElement[0];
        Assert.Equal(holdId, item.GetProperty("holdId").GetString());
        Assert.Equal("EXPIRED", item.GetProperty("status").GetString());
        Assert.Equal("Class 6", item.GetProperty("className").GetString());
        await AssertAvailableSeatsAsync(classId, 3);
    }

    [Fact]
    public async Task GetMyHolds_DoesNotReturnDuplicateApplications()
    {
        var classId = await CreateClassAsync("Class 6", 3);
        var holdId = await CreateHoldAsync(classId, StudentId);

        var response = await GetMyHoldsAsync(StudentId);

        using var document = await ReadJsonAsync(response);
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.Equal(holdId, document.RootElement[0].GetProperty("holdId").GetString());
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

    private async Task<string> CreateHoldAsync(string classId, string studentId)
    {
        var response = await SendWithStudentAsync(
            HttpMethod.Post,
            $"/classes/{classId}/holds",
            studentId);
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response);
        return document.RootElement.GetProperty("holdId").GetString()
            ?? throw new InvalidOperationException("Created hold did not include a holdId.");
    }

    private Task<HttpResponseMessage> ConfirmHoldAsync(string holdId, string? studentId)
    {
        return SendWithStudentAsync(HttpMethod.Post, $"/holds/{holdId}/confirm", studentId);
    }

    private Task<HttpResponseMessage> CancelHoldAsync(string holdId, string? studentId)
    {
        return SendWithStudentAsync(HttpMethod.Delete, $"/holds/{holdId}", studentId);
    }

    private Task<HttpResponseMessage> GetMyHoldsAsync(string? studentId)
    {
        return SendWithStudentAsync(HttpMethod.Get, "/me/holds", studentId);
    }

    private Task<HttpResponseMessage> SendWithStudentAsync(
        HttpMethod method,
        string path,
        string? studentId)
    {
        var request = new HttpRequestMessage(method, path);
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

    private async Task AssertStatusAsync(string holdId, HoldStatus expected)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(expected, (await dbContext.Holds.SingleAsync(hold => hold.Id == holdId)).Status);
    }

    private async Task AssertAvailableSeatsAsync(string classId, int availableSeats)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(
            availableSeats,
            (await dbContext.Classes.SingleAsync(schoolClass => schoolClass.Id == classId)).AvailableSeats);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }
}
