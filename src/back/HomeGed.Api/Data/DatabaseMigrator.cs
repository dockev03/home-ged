using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Data;

/// <summary>Applique les migrations avant que Kestrel n'accepte des requêtes.</summary>
public sealed partial class DatabaseMigrator(
    IServiceScopeFactory scopes, IOptions<DatabaseOptions> options, ILogger<DatabaseMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomeGedDbContext>();
        var pending = string.Join(", ", await db.Database.GetPendingMigrationsAsync(cancellationToken));
        if (pending.Length > 0)
        {
            LogMigrating(logger, pending);
            await db.Database.MigrateAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Base homeged : application des migrations {Migrations}")]
    private static partial void LogMigrating(ILogger logger, string migrations);
}
