var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

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