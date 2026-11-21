using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Paperless;

/// <summary>Disponibilité de Paperless et acceptation de la version d'API figée.</summary>
public sealed class PaperlessHealthCheck(PaperlessClient client, IOptions<PaperlessOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.ProbeAsync(cancellationToken) switch
            {
                PaperlessProbeResult.Ok => HealthCheckResult.Healthy(),
                _ => HealthCheckResult.Unhealthy(
                    $"Paperless refuse la version d'API {options.Value.ApiVersion}."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Paperless injoignable.", ex);
        }
    }
}
