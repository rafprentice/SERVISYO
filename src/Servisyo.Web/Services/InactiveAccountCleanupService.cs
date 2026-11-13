using Microsoft.Extensions.Options;

namespace Servisyo.Web.Services;

/// Database v2.9: the daily clean-up of inactive accounts (the rules are in InactiveAccountService).
/// Runs 2 minutes after the website starts, then every 24 hours (PeriodicTimer). Each run is safe to repeat: the
/// rules come from the data (last sign-in, warning date), not from counting days, so a run that was missed while the
/// site was stopped is simply caught up by the next one.
/// Note: on hosting that stops the website when nobody visits (e.g. the MonsterASP free plan), it only runs while the
/// site is awake; each start runs it once.
public class InactiveAccountCleanupService : BackgroundService
{
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<InactiveAccountOptions> _options;
    private readonly ILogger<InactiveAccountCleanupService> _logger;

    public InactiveAccountCleanupService(IServiceScopeFactory scopes, IOptionsMonitor<InactiveAccountOptions> options,
        ILogger<InactiveAccountCleanupService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);       // let the website finish starting first
            await RunOnceAsync(stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // the website is stopping
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IInactiveAccountService>();
            var deleteEnabled = _options.CurrentValue.DeleteEnabled;
            var result = await service.RunAsync(deleteEnabled, ct);
            if (!deleteEnabled)
                _logger.LogInformation("Inactive accounts (report only): {WouldWarn} would be warned, {OverOneYear} inactive for over a year.",
                    result.WouldWarn, result.OverOneYear);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // never stop the website; the next run tries again
            _logger.LogError(ex, "The inactive-account clean-up failed; it runs again in 24 hours.");
        }
    }
}
