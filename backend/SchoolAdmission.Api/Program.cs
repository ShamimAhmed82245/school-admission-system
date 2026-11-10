using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs.Responses;
using SchoolAdmission.Api.Services.Implementations;
using SchoolAdmission.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "The request is invalid."
                : error.ErrorMessage)
            .Distinct()
            .ToArray();

        if (errors.Length == 0)
        {
            errors = ["The request is invalid."];
        }

        return new BadRequestObjectResult(new ErrorResponse
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Errors = errors
        });
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "School Admission API",
        Version = "v1"
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IClassService, ClassService>();
builder.Services.AddScoped<IHoldService, HoldService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<HoldExpirationService>();
}

var app = builder.Build();

await ApplyMigrationsAsync(app);

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");

        var error = feature?.Error;
        if (error is ClassValidationException or HoldValidationException)
        {
            logger.LogInformation(error, "Request validation failed.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Errors = [error.Message]
            });
            return;
        }

        logger.LogError(feature?.Error, "Unhandled exception while processing a request.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Errors = ["An unexpected error occurred."]
        });
    });
});

app.UseCors("Frontend");
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "School Admission API v1");
});

app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    message = "School Admission API is running"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "SchoolAdmission.Api"
}));

app.Run();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    const int maxAttempts = 10;

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();
            app.Logger.LogInformation("Database migrations applied.");
            return;
        }
        catch (Exception exception) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(
                "Database migration attempt {Attempt} of {MaxAttempts} failed: {Message}",
                attempt,
                maxAttempts,
                exception.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

public partial class Program;
