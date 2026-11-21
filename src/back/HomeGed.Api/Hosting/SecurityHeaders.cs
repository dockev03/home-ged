namespace HomeGed.Api.Hosting;

/// <summary>En-têtes de sécurité HTTP appliqués à toutes les réponses (§4 du cahier des charges).</summary>
public static class SecurityHeaders
{
    /// <summary>
    /// CSP : tout vient de l'application elle-même. `style-src 'unsafe-inline'` est requis
    /// par Naive UI, qui injecte les styles de ses composants à l'exécution (CSS-in-JS).
    /// `blob:` permet l'aperçu des documents téléchargés par le BFF.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-src 'self' blob:; " +
        "object-src 'none'; " +
        "worker-src 'self'; " +
        "manifest-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return next(context);
        });
}
