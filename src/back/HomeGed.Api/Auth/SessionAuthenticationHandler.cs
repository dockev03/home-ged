using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using HomeGed.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Auth;

/// <summary>Session courante, attachée à la requête par <see cref="SessionAuthenticationHandler"/>.</summary>
public sealed record CurrentSession(UserSession Session);

/// <summary>
/// Authentifie la requête par le cookie de session : empreinte du jeton → session active en base.
/// L'expiration glisse à chaque usage (au plus une écriture toutes les 5 minutes).
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISessionStore store,
    IOptions<AuthOptions> authOptions,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "Session";

    public const string SessionIdClaim = "sid";

    internal static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hash = SessionTokens.Hash(Request.Cookies[SessionTokens.CookieName]);
        if (hash is null)
        {
            return AuthenticateResult.NoResult();
        }

        var now = time.GetUtcNow();
        var session = await store.FindActiveAsync(hash, now, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("Session inconnue, expirée ou révoquée.");
        }

        if (now - session.LastSeenAt >= TouchInterval)
        {
            session.LastSeenAt = now;
            session.ExpiresAt = ComputeExpiry(session.CreatedAt, now, authOptions.Value);
            await store.TouchAsync(session.Id, session.LastSeenAt, session.ExpiresAt, Context.RequestAborted);
        }

        Context.Features.Set(new CurrentSession(session));
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, session.Username),
                new Claim(SessionIdClaim, session.Id.ToString()),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>Échéance glissante, plafonnée par la durée de vie maximale.</summary>
    public static DateTimeOffset ComputeExpiry(DateTimeOffset createdAt, DateTimeOffset now, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var idle = now.AddDays(options.IdleTimeoutDays);
        var absolute = createdAt.AddDays(options.AbsoluteLifetimeDays);
        return idle < absolute ? idle : absolute;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Results.Problem(
            statusCode: (int)HttpStatusCode.Unauthorized,
            title: "Connexion requise.",
            extensions: new Dictionary<string, object?> { ["code"] = "non_connecte" })
        .ExecuteAsync(Context);
}
