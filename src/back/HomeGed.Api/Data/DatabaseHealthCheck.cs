using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HomeGed.Api.Data;

/// <summary>Disponibilité de la base homeged.</summary>
public sealed class DatabaseHealthCheck(HomeGedDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Base homeged injoignable.");
}
