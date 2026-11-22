namespace HomeGed.Api.Hosting;

/// <summary>En-têtes de sécurité HTTP appliqués à toutes les réponses (§4 du cahier des charges).</summary>
public static class SecurityHeaders
{
    /// <summary>
    /// CSP : tout vient de l'application elle-même, styles compris (feuilles Bootstrap et Vite ;
    /// les liaisons :style de Vue passent par le CSSOM, que la CSP n'interdit pas).
    /// `blob:` permet l'aperçu des documents téléchargés par le BFF.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self'; " +
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

    /// <summary>
    /// CSP du worker de recadrage (SCAN-02, 03) : OpenCV.js génère ses liaisons JS/WebAssembly à
    /// l'exécution (<c>new Function</c>), ce qui exige <c>'unsafe-eval'</c>. Un worker suit la CSP de
    /// sa propre réponse : la permission reste confinée à ce contexte sans DOM ni réseau, qui ne
    /// reçoit que des pixels. Les pages gardent <see cref="ContentSecurityPolicy"/>.
    /// </summary>
    public const string ScanWorkerContentSecurityPolicy =
        "default-src 'none'; " +
        "script-src 'self' 'unsafe-eval' 'wasm-unsafe-eval'; " +
        "connect-src 'none'";

    /// <summary>Préfixe du script du worker de recadrage buildé par Vite (assets/opencv.worker-[hash].js).</summary>
    public const string ScanWorkerPrefix = "/assets/opencv.worker-";

    /// <summary>Applique la CSP du worker si la réponse est son script ; sans effet sinon.</summary>
    public static void ApplyScanWorkerPolicy(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = context.Request.Path.Value;
        if (path is not null
            && path.StartsWith(ScanWorkerPrefix, StringComparison.Ordinal)
            && path.EndsWith(".js", StringComparison.Ordinal)
            && path.IndexOf('/', ScanWorkerPrefix.Length) < 0)
        {
            context.Response.Headers.ContentSecurityPolicy = ScanWorkerContentSecurityPolicy;
        }
    }

    /// <summary>
    /// Autorise l'affichage de cette réponse dans un cadre des pages Home-GED, et d'elles seules
    /// (aperçu des documents dans la fiche). Toutes les autres réponses restent interdites de cadre.
    /// </summary>
    public static void AllowSameOriginFraming(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.ContentSecurityPolicy =
            ContentSecurityPolicy.Replace("frame-ancestors 'none'", "frame-ancestors 'self'", StringComparison.Ordinal);
        response.Headers.XFrameOptions = "SAMEORIGIN";
    }

    /// <summary>Contenu non maîtrisé : document isolé (origine opaque, aucun script).</summary>
    public static void Sandbox(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy + "; sandbox";
    }

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
