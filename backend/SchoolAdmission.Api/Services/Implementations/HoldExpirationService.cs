using SchoolAdmission.Api.Services.Interfaces;

namespace SchoolAdmission.Api.Services.Implementations;

public sealed class HoldExpirationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldExpirationService> _logger;

    public HoldExpirationService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var holdService = scope.ServiceProvider.GetRequiredService<IHoldService>();
                var expired = await holdService.ExpireDueHoldsAsync(stoppingToken);
                if (expired > 0)
                {
                    _logger.LogInformation(
                        "Released seats for {ExpiredHoldCount} expired holds.",
                        expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to expire due holds.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
