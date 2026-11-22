using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Demande le token de l'utilisateur (voir docs/poc/auth-2fa.md). L'IP du client est transmise
    /// dans X-Forwarded-For pour que la limite de Paperless s'applique par appareil, pas à tout le foyer.
    /// </summary>
    public async Task<TokenResult> RequestTokenAsync(
        string username, string password, string? code, string? clientIp, CancellationToken cancellationToken)
    {
        // Corps de longueur connue : Paperless ignore un corps envoyé en Transfer-Encoding: chunked
        // (comportement par défaut de JsonContent) et répond « This field is required ».
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/token/", UriKind.Relative))
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(
                new TokenRequest(username, password, string.IsNullOrEmpty(code) ? null : code)))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };
        if (!string.IsNullOrEmpty(clientIp))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
                return string.IsNullOrEmpty(body?.Token)
                    ? throw new HttpRequestException("Réponse de Paperless sans token.")
                    : new TokenResult(TokenOutcome.Success, body.Token);

            case HttpStatusCode.TooManyRequests:
                return new TokenResult(TokenOutcome.Throttled, RetryAfter: response.Headers.RetryAfter?.Delta);

            case HttpStatusCode.BadRequest:
                var errors = await ReadErrorsAsync(response, cancellationToken);
                if (errors.Contains(MfaRequiredMessage, StringComparer.Ordinal))
                {
                    return new TokenResult(TokenOutcome.MfaRequired);
                }

                return errors.Contains(InvalidMfaMessage, StringComparer.Ordinal)
                    ? new TokenResult(TokenOutcome.InvalidMfaCode)
                    : new TokenResult(TokenOutcome.InvalidCredentials);

            default:
                throw new HttpRequestException(
                    $"Réponse inattendue de Paperless à la demande de token (HTTP {(int)response.StatusCode}).",
                    null, response.StatusCode);
        }
    }

    /// <summary>Identité de l'utilisateur à qui appartient le token (<c>GET /api/ui_settings/</c>).</summary>
    public async Task<PaperlessUser> GetCurrentUserAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/ui_settings/", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Lecture de l'utilisateur Paperless impossible (HTTP {(int)response.StatusCode}).",
                null, response.StatusCode);
        }

        var settings = await response.Content.ReadFromJsonAsync<UiSettingsResponse>(cancellationToken);
        return settings?.User is { Id: > 0, Username.Length: > 0 } user
            ? new PaperlessUser(user.Id, user.Username)
            : throw new HttpRequestException("Réponse de Paperless sans utilisateur.");
    }

    /// <summary>GET authentifié par le token de l'utilisateur (voir <see cref="SendAsync"/>).</summary>
    public Task<HttpResponseMessage> GetAsync(string token, string relativeUri, CancellationToken cancellationToken) =>
        SendAsync(token, HttpMethod.Get, relativeUri, null, cancellationToken);

    /// <summary>
    /// Requête authentifiée par le token de l'utilisateur : les droits sont appliqués par Paperless.
    /// La réponse est rendue dès les en-têtes reçus (flux) ; l'appelant la libère.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        string token, HttpMethod method, string relativeUri, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(relativeUri, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        if (body is not null)
        {
            // Longueur connue : Paperless ignore un corps envoyé en Transfer-Encoding: chunked.
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            };
        }

        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Messages de Paperless 3.5 (sans Accept-Language, ils restent en anglais).</summary>
    internal const string MfaRequiredMessage = "MFA code is required";

    internal const string InvalidMfaMessage = "Invalid MFA code";

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken);
            return body?.Values
                .Where(v => v.ValueKind == JsonValueKind.Array)
                .SelectMany(v => v.EnumerateArray())
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record TokenRequest(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code);

    private sealed record TokenResponse([property: JsonPropertyName("token")] string? Token);

    private sealed record UiSettingsResponse([property: JsonPropertyName("user")] UiSettingsUser? User);

    private sealed record UiSettingsUser(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("username")] string Username);

    [LoggerMessage(Level = LogLevel.Error, Message = "Paperless refuse la version d'API demandée (HTTP 406)")]
    private static partial void LogApiVersionRejected(ILogger logger);
}
