using System.Globalization;
using System.Net;
using System.Security.Claims;
using HomeGed.Api.Data;
using HomeGed.Api.Paperless;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Auth;

public sealed record LoginRequest(string? Username, string? Password, string? Code);

public sealed record CurrentUser(int Id, string Username);

public sealed record SessionInfo(
    Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt,
    string? Ip, string? UserAgent, bool Current);

/// <summary>Connexion avec les identifiants Paperless, sessions révocables (§3.2, H8).</summary>
public static partial class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync);
        auth.MapGet("/me", (ClaimsPrincipal user) => TypedResults.Ok(new CurrentUser(UserId(user), user.Identity!.Name!)));
        auth.MapGet("/sessions", ListSessionsAsync);
        auth.MapDelete("/sessions/{id:guid}", RevokeSessionAsync);
        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest body,
        HttpContext context,
        PaperlessClient paperless,
        ISessionStore store,
        PaperlessTokenProtector protector,
        LoginThrottle throttle,
        IOptions<AuthOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var logger = loggers.CreateLogger(typeof(AuthEndpoints));
        var username = body.Username?.Trim() ?? "";
        var password = body.Password ?? "";
        var code = string.Concat((body.Code ?? "").Where(c => !char.IsWhiteSpace(c)));
        if (username.Length is 0 or > 150 || password.Length is 0 or > 1024
            || code.Length > 10 || !code.All(char.IsAsciiDigit))
        {
            return Problem(HttpStatusCode.BadRequest, "Identifiant, mot de passe ou code invalide.", "requete_invalide");
        }

        var ip = ClientIp(context);
        string[] keys = [LoginThrottle.IpKey(ip), LoginThrottle.UserKey(username)];
        if (throttle.GetRetryAfter(keys) is { } blockedFor)
        {
            LogBlocked(logger, username, ip);
            return TooManyAttempts(context, blockedFor);
        }

        TokenResult result;
        PaperlessUser? user = null;
        try
        {
            result = await paperless.RequestTokenAsync(username, password, code, ip, cancellationToken);
            if (result.Outcome == TokenOutcome.Success)
            {
                user = await paperless.GetCurrentUserAsync(result.Token!, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogPaperlessUnavailable(logger, ex);
            return Problem(HttpStatusCode.ServiceUnavailable, "Paperless ne répond pas, réessayez plus tard.", "paperless_indisponible");
        }

        switch (result.Outcome)
        {
            case TokenOutcome.MfaRequired:
                // Mot de passe correct : Paperless ne demande le code qu'après l'avoir vérifié.
                return Problem(HttpStatusCode.Unauthorized, "Saisissez le code de vérification.", "mfa_requis");

            case TokenOutcome.Throttled:
                LogFailure(logger, username, ip, "limite Paperless");
                return TooManyAttempts(context, result.RetryAfter ?? TimeSpan.FromMinutes(1));

            case TokenOutcome.InvalidMfaCode:
            case TokenOutcome.InvalidCredentials:
                var reason = result.Outcome == TokenOutcome.InvalidMfaCode ? "code_invalide" : "identifiants_invalides";
                LogFailure(logger, username, ip, reason);
                throttle.RecordFailure(keys);
                return reason == "code_invalide"
                    ? Problem(HttpStatusCode.Unauthorized, "Code de vérification incorrect.", reason)
                    : Problem(HttpStatusCode.Unauthorized, "Identifiant ou mot de passe incorrect.", reason);
        }

        var now = time.GetUtcNow();
        var token = SessionTokens.NewToken();
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            TokenHash = SessionTokens.Hash(token)!,
            UserId = user!.Id,
            Username = user.Username,
            PaperlessToken = protector.Protect(result.Token!),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = SessionAuthenticationHandler.ComputeExpiry(now, now, options.Value),
            Ip = ip,
            UserAgent = Truncate(context.Request.Headers.UserAgent.ToString(), 512),
        };
        await store.AddAsync(session, cancellationToken);
        throttle.Reset(keys);

        context.Response.Cookies.Append(SessionTokens.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            // Plafond navigateur ; l'inactivité est contrôlée côté serveur.
            Expires = now.AddDays(options.Value.AbsoluteLifetimeDays),
        });
        LogSuccess(logger, user.Username, ip);
        return TypedResults.Ok(new CurrentUser(user.Id, user.Username));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, ClaimsPrincipal user, ISessionStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        await store.RevokeAsync(SessionId(user), UserId(user), time.GetUtcNow(), cancellationToken);
        DeleteCookie(context);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListSessionsAsync(
        ClaimsPrincipal user, ISessionStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        var current = SessionId(user);
        var sessions = await store.ListActiveAsync(UserId(user), time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(sessions.Select(s => new SessionInfo(
            s.Id, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.Ip, s.UserAgent, s.Id == current)));
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid id, HttpContext context, ClaimsPrincipal user, ISessionStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        // Une session d'un autre utilisateur répond comme une session inexistante.
        if (!await store.RevokeAsync(id, UserId(user), time.GetUtcNow(), cancellationToken))
        {
            return Problem(HttpStatusCode.NotFound, "Session introuvable.", "session_introuvable");
        }

        if (id == SessionId(user))
        {
            DeleteCookie(context);
        }

        return TypedResults.NoContent();
    }

    /// <summary>IP du client (après UseForwardedHeaders : celle fournie par NPM).</summary>
    internal static string? ClientIp(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    private static int UserId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    private static Guid SessionId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(SessionAuthenticationHandler.SessionIdClaim)!);

    private static void DeleteCookie(HttpContext context) =>
        context.Response.Cookies.Delete(SessionTokens.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });

    private static IResult TooManyAttempts(HttpContext context, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        var minutes = (int)Math.Ceiling(seconds / 60.0);
        return Problem(
            HttpStatusCode.TooManyRequests,
            minutes <= 1
                ? "Trop de tentatives, réessayez dans une minute."
                : $"Trop de tentatives, réessayez dans {minutes} minutes.",
            "trop_de_tentatives");
    }

    private static IResult Problem(HttpStatusCode status, string title, string code) =>
        Results.Problem(statusCode: (int)status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static string? Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Information, Message = "Connexion de {Username} depuis {Ip}")]
    private static partial void LogSuccess(ILogger logger, string username, string? ip);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de connexion de {Username} depuis {Ip} : {Reason}")]
    private static partial void LogFailure(ILogger logger, string username, string? ip, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connexion bloquée (trop d'échecs) pour {Username} depuis {Ip}")]
    private static partial void LogBlocked(ILogger logger, string username, string? ip);

    [LoggerMessage(Level = LogLevel.Error, Message = "Paperless injoignable pendant une connexion")]
    private static partial void LogPaperlessUnavailable(ILogger logger, Exception ex);
}
