namespace HomeGed.Api.Paperless;

/// <summary>Issue d'une demande de token à Paperless (<c>POST /api/token/</c>).</summary>
public enum TokenOutcome
{
    Success,

    /// <summary>Identifiants corrects, mais le compte a le 2FA : il faut le code TOTP.</summary>
    MfaRequired,

    /// <summary>Code TOTP faux (les codes de secours ne sont pas acceptés par l'API).</summary>
    InvalidMfaCode,

    InvalidCredentials,

    /// <summary>Limite de tentatives de Paperless atteinte (HTTP 429).</summary>
    Throttled,
}

public sealed record TokenResult(TokenOutcome Outcome, string? Token = null, TimeSpan? RetryAfter = null);

/// <summary>Utilisateur Paperless authentifié par son token.</summary>
public sealed record PaperlessUser(int Id, string Username);
