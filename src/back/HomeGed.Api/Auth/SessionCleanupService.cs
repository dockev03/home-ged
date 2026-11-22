namespace HomeGed.Api.Auth;

/// <summary>Supprime régulièrement les sessions expirées ou révoquées depuis plus de 30 jours.</summary>
public sealed partial class SessionCleanupService(
    IServiceScopeFactory scopes, TimeProvider time, ILogger<SessionCleanupService> logger) : BackgroundService
{
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly TimeSpan Period = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
                var deleted = await store.PurgeAsync(time.GetUtcNow() - Retention, stoppingToken);
                if (deleted > 0)
                {
                    LogPurged(logger, deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPurgeFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} session(s) périmée(s) supprimée(s)")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nettoyage des sessions impossible")]
    private static partial void LogPurgeFailed(ILogger logger, Exception ex);
}
