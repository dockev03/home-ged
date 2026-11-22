using System.Net;

namespace HomeGed.Api.Auth;

/// <summary>
/// Protection CSRF en complément du cookie SameSite=Strict : toute requête /api qui modifie
/// quelque chose doit porter l'en-tête X-Requested-With. Un formulaire d'un autre site ne peut pas
/// l'ajouter, et fetch() ne le peut qu'après un preflight CORS, que Home-GED n'autorise pas.
/// </summary>
public static class AntiForgery
{
    public const string HeaderName = "X-Requested-With";

    public static IApplicationBuilder UseApiAntiForgery(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (request.Path.StartsWithSegments("/api", StringComparison.Ordinal)
                && !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
                && string.IsNullOrEmpty(request.Headers[HeaderName]))
            {
                await Results.Problem(
                    statusCode: (int)HttpStatusCode.Forbidden,
                    title: "Requête refusée.",
                    extensions: new Dictionary<string, object?> { ["code"] = "en_tete_manquant" })
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });
}
