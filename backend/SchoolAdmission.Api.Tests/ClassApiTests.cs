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
public sealed class ClassApiTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ClassApiTests(ApiFactory factory)
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
    public async Task CreateClass_WithValidInput_Returns201_AndInitializesAvailableSeats()
    {
        var response = await _client.PostAsJsonAsync("/classes", new
        {
            name = "  Class 6 - Morning Shift  ",
            totalSeats = 120
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = await ReadJsonAsync(response);
        Assert.Equal(
            ["id", "name", "totalSeats"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());

        var id = document.RootElement.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.StartsWith("c_", id, StringComparison.Ordinal);
        Assert.Equal("Class 6 - Morning Shift", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(120, document.RootElement.GetProperty("totalSeats").GetInt32());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await dbContext.Classes.SingleAsync(schoolClass => schoolClass.Id == id);
        Assert.Equal(120, stored.TotalSeats);
        Assert.Equal(120, stored.AvailableSeats);
        Assert.Equal("Class 6 - Morning Shift", stored.Name);
    }

    [Theory]
    [InlineData("", 10, "Name is required.")]
    [InlineData("   ", 10, "Name is required.")]
    [InlineData("Class 6", 0, "Total seats must be greater than zero.")]
    [InlineData("Class 6", -5, "Total seats must be greater than zero.")]
    public async Task CreateClass_WithInvalidInput_Returns400(
        string name,
        int totalSeats,
        string expectedError)
    {
        var response = await _client.PostAsJsonAsync("/classes", new
        {
            name,
            totalSeats
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = await ReadJsonAsync(response);
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Bad Request", document.RootElement.GetProperty("title").GetString());
        var errors = document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetString())
            .ToArray();
        Assert.Contains(expectedError, errors);
    }

    [Fact]
    public async Task GetClasses_WhenDatabaseIsEmpty_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/classes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetClasses_ReturnsPersistedAvailableSeats_AndHoldStatistics()
    {
        var first = await CreateClassAsync("Class 6 - Morning Shift", 120);
        var second = await CreateClassAsync("Class 7 - Day Shift", 40);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Holds.AddRange(
                Hold(first, "10000000000000001", HoldStatus.HELD, now.AddMinutes(10)),
                Hold(first, "10000000000000002", HoldStatus.HELD, now.AddMinutes(-5)),
                Hold(first, "10000000000000003", HoldStatus.CONFIRMED, now.AddMinutes(10)),
                Hold(first, "10000000000000004", HoldStatus.CONFIRMED, now.AddMinutes(-30)),
                Hold(first, "10000000000000005", HoldStatus.CANCELLED, now.AddMinutes(10)),
                Hold(first, "10000000000000006", HoldStatus.EXPIRED, now.AddMinutes(-1)),
                Hold(second, "10000000000000007", HoldStatus.HELD, now.AddMinutes(5)));

            var storedFirst = await dbContext.Classes.SingleAsync(schoolClass => schoolClass.Id == first);
            storedFirst.AvailableSeats = 25;
            await dbContext.SaveChangesAsync();
        }

        var listResponse = await _client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = await ReadJsonAsync(listResponse);
        Assert.Equal(2, listDocument.RootElement.GetArrayLength());

        var firstItem = FindClass(listDocument.RootElement, first);
        AssertSeatStatistics(firstItem, "Class 6 - Morning Shift", 120, 1, 2, 25);

        var secondItem = FindClass(listDocument.RootElement, second);
        AssertSeatStatistics(secondItem, "Class 7 - Day Shift", 40, 1, 0, 40);

        var byIdResponse = await _client.GetAsync($"/classes/{first}");
        Assert.Equal(HttpStatusCode.OK, byIdResponse.StatusCode);
        using var byIdDocument = await ReadJsonAsync(byIdResponse);
        AssertSeatStatistics(
            byIdDocument.RootElement,
            "Class 6 - Morning Shift",
            120,
            1,
            2,
            25);
    }

    [Fact]
    public async Task GetClassById_WhenClassDoesNotExist_Returns404()
    {
        var response = await _client.GetAsync("/classes/c_missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "Class not found.",
            document.RootElement.GetProperty("errors")[0].GetString());
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

    private static Hold Hold(
        string classId,
        string studentId,
        HoldStatus status,
        DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new Hold
        {
            Id = $"h_{Guid.NewGuid():N}",
            StudentId = studentId,
            ClassId = classId,
            Status = status,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static JsonElement FindClass(JsonElement array, string id)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == id)
            {
                return item;
            }
        }

        throw new Xunit.Sdk.XunitException($"Class {id} was not returned.");
    }

    private static void AssertSeatStatistics(
        JsonElement item,
        string name,
        int totalSeats,
        int heldSeats,
        int confirmedSeats,
        int availableSeats)
    {
        Assert.Equal(
            ["id", "name", "totalSeats", "heldSeats", "confirmedSeats", "availableSeats"],
            item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(name, item.GetProperty("name").GetString());
        Assert.Equal(totalSeats, item.GetProperty("totalSeats").GetInt32());
        Assert.Equal(heldSeats, item.GetProperty("heldSeats").GetInt32());
        Assert.Equal(confirmedSeats, item.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(availableSeats, item.GetProperty("availableSeats").GetInt32());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }
}

[CollectionDefinition("class-api")]
public sealed class ClassApiCollection : ICollectionFixture<ApiFactory>;
