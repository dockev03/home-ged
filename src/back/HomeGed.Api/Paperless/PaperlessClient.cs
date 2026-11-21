using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace HomeGed.Api.Paperless;

/// <summary>Résultat du contrôle de disponibilité de Paperless.</summary>
public enum PaperlessProbeResult
{
    /// <summary>Paperless répond et accepte la version d'API figée.</summary>
    Ok,

    /// <summary>Paperless répond mais refuse la version d'API figée (HTTP 406).</summary>
    ApiVersionRejected,
}

/// <summary>
/// Client typé de l'API REST Paperless. Seul point de contact avec Paperless :
/// jamais d'accès à sa base ni à son dossier media.
/// </summary>
public sealed partial class PaperlessClient(HttpClient http, ILogger<PaperlessClient> logger)
{
    /// <summary>Configure l'HttpClient : adresse de base, version d'API figée, délai.</summary>
    public static void Configure(HttpClient http, PaperlessOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        http.DefaultRequestHeaders.Accept.Clear();
        var accept = new MediaTypeWithQualityHeaderValue("application/json");
        accept.Parameters.Add(new NameValueHeaderValue(
            "version", options.ApiVersion.ToString(CultureInfo.InvariantCulture)));
        http.DefaultRequestHeaders.Accept.Add(accept);
    }

    /// <summary>
    /// Contrôle sans authentification : Paperless n'envoie ses en-têtes de version
    /// qu'aux requêtes authentifiées, mais il valide la version de l'en-tête Accept
    /// avant l'authentification. 401 = joignable et version acceptée ; 406 = version refusée.
    /// </summary>
    public async Task<PaperlessProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            new Uri("api/documents/?page_size=1", UriKind.Relative), cancellationToken);
        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.NotAcceptable)
        {
            LogApiVersionRejected(logger);
            return PaperlessProbeResult.ApiVersionRejected;
        }

        if (status >= 500 || status is >= 300 and < 400)
        {
            throw new HttpRequestException(
                $"Réponse inattendue de Paperless (HTTP {status}).", null, response.StatusCode);
        }

        return PaperlessProbeResult.Ok;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Paperless refuse la version d'API demandée (HTTP 406)")]
    private static partial void LogApiVersionRejected(ILogger logger);
}
