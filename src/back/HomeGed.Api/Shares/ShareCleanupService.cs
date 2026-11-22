namespace HomeGed.Api.Shares;

/// <summary>
/// Purge des liens de partage (SHARE-07) : supprime les liens expirés ou révoqués depuis plus de 30 jours,
/// avec leur journal d'accès (gardé un mois pour un éventuel contrôle, puis effacé).
/// </summary>
public sealed partial class ShareCleanupService(
    IServiceScopeFactory scopes, TimeProvider time, ILogger<ShareCleanupService> logger) : BackgroundService
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
                var store = scope.ServiceProvider.GetRequiredService<IShareLinkStore>();
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

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} lien(s) de partage périmé(s) supprimé(s)")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Purge des liens de partage impossible")]
    private static partial void LogPurgeFailed(ILogger logger, Exception ex);
}
