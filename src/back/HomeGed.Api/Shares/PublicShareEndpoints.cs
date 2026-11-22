using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Data;
using HomeGed.Api.Documents;
using HomeGed.Api.Hosting;
using HomeGed.Api.Paperless;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Shares;

/// <summary>
/// Page publique d'un lien de partage (SHARE-03), sans session : mot de passe, puis aperçu et
/// téléchargement. Le fichier est lu dans Paperless avec le compte de service en lecture seule (§3.2)
/// et transite par Home-GED. Chaque vérification de mot de passe est journalisée (SHARE-05) ; 5 échecs
/// bloquent le lien et l'IP pendant 15 minutes (SHARE-06).
/// <para>
/// Réponse unique pour un lien inexistant, expiré, révoqué, épuisé ou un mauvais mot de passe : rien
/// ne révèle si le lien existe. Un lien inexistant coûte le même calcul Argon2id qu'un lien réel.
/// </para>
/// </summary>
public static partial class PublicShareEndpoints
{
    public const string UnavailableCode = "lien_indisponible";

    public const string UnavailableMessage = "Lien indisponible ou mot de passe incorrect.";

    /// <summary>Empreinte d'un mot de passe aléatoire jamais divulgué, vérifiée pour les liens inexistants.</summary>
    private static readonly Lazy<string> DummyHash = new(() => SharePasswordHasher.Hash(ShareLinkIds.New()));

    /// <summary>Ce que voit le destinataire : rien d'autre que le titre et les limites du lien.</summary>
    public sealed record PublicShareDto(string Titre, DateTimeOffset ExpireLe, int? TelechargementsRestants);

    public sealed record AccessRequest(string? MotDePasse);

    public static RouteGroupBuilder MapPublicShares(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/public/partages/{id}").AllowAnonymous();
        group.MapPost("/acces", AccessAsync);
        group.MapGet("", GetAsync);
        group.MapGet("/apercu", PreviewAsync);
        group.MapGet("/telechargement", DownloadAsync);
        return api;
    }

    /// <summary>Signale au démarrage un compte de service non configuré : les liens ne s'ouvriraient pas.</summary>
    public static void WarnIfNotConfigured(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(app.Services.GetRequiredService<IOptions<ShareOptions>>().Value.ServiceToken))
        {
            LogNotConfigured(app.Logger);
        }
    }

    internal static string LinkKey(string id) => "partage:" + id;

    internal static string IpKey(string? ip) => "partage-ip:" + (ip ?? "inconnue");

    /// <summary>Vérifie le mot de passe et, s'il est bon et le lien utilisable, délivre le ticket d'accès.</summary>
    private static async Task<IResult> AccessAsync(
        string id,
        AccessRequest? body,
        HttpContext context,
        IShareLinkStore store,
        LoginThrottle throttle,
        ShareTickets tickets,
        PaperlessClient paperless,
        IOptions<ShareOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        var logger = loggers.CreateLogger(typeof(PublicShareEndpoints));
        if (ServiceToken(options, logger) is not { } token)
        {
            return ServiceUnavailable();
        }

        var ip = AuthEndpoints.ClientIp(context);
        string[] keys = [LinkKey(id), IpKey(ip)];
        if (throttle.GetRetryAfter(keys) is { } blockedFor)
        {
            LogBlocked(logger, ip);
            return AuthEndpoints.TooManyAttempts(context, blockedFor);
        }

        var password = body?.MotDePasse ?? "";
        var link = ShareLinkIds.IsValid(id) ? await store.FindAsync(id, cancellationToken) : null;

        // Le mot de passe est toujours vérifié (empreinte factice si le lien n'existe pas) : même coût, même réponse.
        var passwordOk = password.Length <= SharePasswordHasher.MaxLength
            && SharePasswordHasher.Verify(password, link?.PasswordHash ?? DummyHash.Value)
            && link is not null;

        PublicShareDto? dto = null;
        IResult? failure = null;
        if (passwordOk && IsUsable(link!, time.GetUtcNow()))
        {
            (dto, failure) = await DescribeAsync(paperless, token, link!, logger, cancellationToken);
        }

        if (link is not null)
        {
            await store.LogAccessAsync(
                new ShareAccessLog
                {
                    ShareLinkId = link.Id,
                    Timestamp = time.GetUtcNow(),
                    Ip = ip,
                    UserAgent = Truncate(context.Request.Headers.UserAgent.ToString(), 512),
                    Success = dto is not null,
                },
                cancellationToken);
        }

        if (!passwordOk)
        {
            throttle.RecordFailure(keys);
            return Unavailable();
        }

        // Bon mot de passe : on repart de zéro pour ce lien (pas pour l'IP, qui peut en essayer d'autres).
        throttle.Reset(LinkKey(id));
        if (dto is null)
        {
            return failure ?? Unavailable();
        }

        tickets.Issue(context, link!.Id, link.ExpiresAt);
        return TypedResults.Ok(dto);
    }

    /// <summary>Informations du lien pour un destinataire qui a déjà saisi le mot de passe (rechargement de la page).</summary>
    private static async Task<IResult> GetAsync(
        string id,
        HttpContext context,
        IShareLinkStore store,
        ShareTickets tickets,
        PaperlessClient paperless,
        IOptions<ShareOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        var logger = loggers.CreateLogger(typeof(PublicShareEndpoints));
        var (link, token, refused) = await AuthorizeAsync(id, context, store, tickets, options, time, logger, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        var (dto, failure) = await DescribeAsync(paperless, token!, link!, logger, cancellationToken);
        return dto is not null ? TypedResults.Ok(dto) : failure ?? Unavailable();
    }

    /// <summary>Aperçu (version archivée ou image) : ne compte pas comme un téléchargement.</summary>
    private static async Task<IResult> PreviewAsync(
        string id,
        HttpContext context,
        IShareLinkStore store,
        ShareTickets tickets,
        PaperlessClient paperless,
        IOptions<ShareOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        var logger = loggers.CreateLogger(typeof(PublicShareEndpoints));
        var (link, token, refused) = await AuthorizeAsync(id, context, store, tickets, options, time, logger, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        var (response, failure) = await OpenAsync(paperless, token!, DocumentPath(link!.DocumentId, "preview/"), logger, cancellationToken);
        return response is null ? failure! : await StreamAsync(context, response, framable: true, cancellationToken);
    }

    /// <summary>
    /// Téléchargement : le fichier est d'abord demandé à Paperless, puis le téléchargement est compté
    /// de façon atomique ; le quota n'est jamais dépassé, même avec des demandes simultanées.
    /// </summary>
    private static async Task<IResult> DownloadAsync(
        string id,
        HttpContext context,
        IShareLinkStore store,
        ShareTickets tickets,
        PaperlessClient paperless,
        IOptions<ShareOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        var logger = loggers.CreateLogger(typeof(PublicShareEndpoints));
        var (link, token, refused) = await AuthorizeAsync(id, context, store, tickets, options, time, logger, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        var (response, failure) = await OpenAsync(paperless, token!, DocumentPath(link!.DocumentId, "download/"), logger, cancellationToken);
        if (response is null)
        {
            return failure!;
        }

        if (!await store.TryCountDownloadAsync(link.Id, time.GetUtcNow(), cancellationToken))
        {
            response.Dispose();
            return Unavailable();
        }

        return await StreamAsync(context, response, framable: false, cancellationToken);
    }

    /// <summary>Ticket valide pour ce lien, et lien toujours utilisable (revérifié à chaque requête).</summary>
    private static async Task<(ShareLink? Link, string? Token, IResult? Refused)> AuthorizeAsync(
        string id,
        HttpContext context,
        IShareLinkStore store,
        ShareTickets tickets,
        IOptions<ShareOptions> options,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (ServiceToken(options, logger) is not { } token)
        {
            return (null, null, ServiceUnavailable());
        }

        if (!ShareLinkIds.IsValid(id) || !tickets.IsValid(context, id))
        {
            return (null, null, PaperlessRelay.Problem(HttpStatusCode.Unauthorized, "Saisissez le mot de passe du lien.", "mot_de_passe_requis"));
        }

        var link = await store.FindAsync(id, cancellationToken);
        if (link is null || !IsUsable(link, time.GetUtcNow()))
        {
            ShareTickets.Delete(context, id);
            return (null, null, Unavailable());
        }

        return (link, token, null);
    }

    internal static bool IsUsable(ShareLink link, DateTimeOffset now) =>
        link.RevokedAt is null && link.ExpiresAt > now
        && (link.MaxDownloads is null || link.DownloadCount < link.MaxDownloads);

    /// <summary>Titre du document, lu par le compte de service ; un document qu'il ne voit pas rend le lien indisponible.</summary>
    private static async Task<(PublicShareDto? Dto, IResult? Failure)> DescribeAsync(
        PaperlessClient paperless, string token, ShareLink link, ILogger logger, CancellationToken cancellationToken)
    {
        var (response, failure) = await OpenAsync(
            paperless, token, string.Create(CultureInfo.InvariantCulture, $"api/documents/{link.DocumentId}/?fields=id,title"), logger, cancellationToken);
        if (response is null)
        {
            return (null, failure);
        }

        using (response)
        {
            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
                var title = body?["title"] is JsonValue t && t.TryGetValue<string>(out var value) ? value : "";
                int? remaining = link.MaxDownloads is { } max ? Math.Max(0, max - link.DownloadCount) : null;
                return (new PublicShareDto(title, link.ExpiresAt, remaining), null);
            }
            catch (JsonException ex)
            {
                LogPaperlessUnavailable(logger, ex);
                return (null, ServiceUnavailable());
            }
        }
    }

    /// <summary>
    /// GET avec le token du compte de service. Succès : la réponse (en flux, à libérer par l'appelant).
    /// 403/404 : lien indisponible. Token refusé ou Paperless injoignable : 503.
    /// </summary>
    private static async Task<(HttpResponseMessage? Response, IResult? Failure)> OpenAsync(
        PaperlessClient paperless, string token, string uri, ILogger logger, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await paperless.GetAsync(token, uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var ok = response;
                response = null;
                return (ok, null);
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound or HttpStatusCode.Forbidden:
                    return (null, Unavailable());
                case HttpStatusCode.Unauthorized:
                    LogServiceTokenRejected(logger);
                    return (null, ServiceUnavailable());
                default:
                    LogPaperlessUnavailable(logger, new HttpRequestException(
                        $"Réponse inattendue de Paperless (HTTP {(int)response.StatusCode}).", null, response.StatusCode));
                    return (null, ServiceUnavailable());
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogPaperlessUnavailable(logger, ex);
            return (null, ServiceUnavailable());
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>Relaie le fichier en flux ; l'aperçu n'est affichable que dans un cadre de Home-GED, et isolé s'il n'est pas inerte.</summary>
    private static async Task<IResult> StreamAsync(
        HttpContext context, HttpResponseMessage response, bool framable, CancellationToken cancellationToken)
    {
        context.Response.RegisterForDispose(response);
        var content = response.Content.Headers;
        if (content.ContentDisposition is { } disposition)
        {
            context.Response.Headers.ContentDisposition = disposition.ToString();
        }

        if (content.ContentLength is { } length)
        {
            context.Response.ContentLength = length;
        }

        var type = content.ContentType?.MediaType ?? "application/octet-stream";
        if (framable && DocumentEndpoints.IsInertPreviewType(type))
        {
            SecurityHeaders.AllowSameOriginFraming(context.Response);
        }
        else if (framable)
        {
            SecurityHeaders.Sandbox(context.Response);
        }

        return TypedResults.Stream(await response.Content.ReadAsStreamAsync(cancellationToken), type);
    }

    private static string DocumentPath(int documentId, string suffix) =>
        string.Create(CultureInfo.InvariantCulture, $"api/documents/{documentId}/{suffix}");

    private static string? ServiceToken(IOptions<ShareOptions> options, ILogger logger)
    {
        var token = options.Value.ServiceToken.Trim();
        if (token.Length == 0)
        {
            LogServiceTokenMissing(logger);
            return null;
        }

        return token;
    }

    /// <summary>Jamais en cache (navigateur, proxy) ni indexé par un moteur de recherche.</summary>
    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    private static IResult Unavailable() =>
        PaperlessRelay.Problem(HttpStatusCode.NotFound, UnavailableMessage, UnavailableCode);

    private static IResult ServiceUnavailable() =>
        PaperlessRelay.Problem(
            HttpStatusCode.ServiceUnavailable, "Le partage de documents est momentanément indisponible, réessayez plus tard.", "partage_indisponible");

    private static string? Truncate(string value, int max) =>
        value.Length == 0 ? null : value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lien de partage : tentatives bloquées pour {Ip}")]
    private static partial void LogBlocked(ILogger logger, string? ip);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Share__ServiceToken non renseigné : les liens de partage ne pourront pas être ouverts")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lien de partage : token du compte de service absent (Share__ServiceToken)")]
    private static partial void LogServiceTokenMissing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lien de partage : Paperless refuse le token du compte de service")]
    private static partial void LogServiceTokenRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lien de partage : Paperless injoignable")]
    private static partial void LogPaperlessUnavailable(ILogger logger, Exception ex);
}
