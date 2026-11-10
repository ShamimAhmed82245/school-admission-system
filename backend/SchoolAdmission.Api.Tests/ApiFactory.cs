using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace SchoolAdmission.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string? _externalConnectionString =
        Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING");

    private PostgreSqlContainer? _container;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("HOLD_DURATION_SECONDS", "600");

        if (!string.IsNullOrWhiteSpace(_externalConnectionString))
        {
            await EnsureDatabaseExistsAsync(_externalConnectionString);
            _connectionString = _externalConnectionString;
        }
        else
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            _connectionString);
    }

    public new async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("The test database has not been started.");
        }

        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("HOLD_DURATION_SECONDS", "600");
        builder.UseEnvironment("Testing");
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                "TEST_CONNECTION_STRING must include a database name.");
        }

        builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var check = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name",
            connection);
        check.Parameters.AddWithValue("name", databaseName);
        var exists = await check.ExecuteScalarAsync();
        if (exists is not null)
        {
            return;
        }

        var quotedName = "\"" + databaseName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        await using var create = new NpgsqlCommand($"CREATE DATABASE {quotedName}", connection);
        await create.ExecuteNonQueryAsync();
    }
}
