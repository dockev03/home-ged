using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HomeGed.Api.Auth;
using HomeGed.Api.Data;
using HomeGed.Api.Documents;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Shares;

/// <summary>
/// Liens de partage protégés, côté propriétaire (SHARE-01, 02, 04) : création, liste, révocation.
/// Journal d'accès de chaque lien (SHARE-05). La page publique est dans <see cref="PublicShareEndpoints"/>.
/// </summary>
public static class ShareEndpoints
{
    /// <summary>Plafond du nombre de téléchargements autorisés sur un lien.</summary>
    public const int MaxDownloadsLimit = 1000;

    public sealed record ShareSettings(int JoursMax);

    public sealed record CreateShareRequest(JsonNode? Document, string? MotDePasse, JsonNode? Jours, JsonNode? TelechargementsMax);

    public sealed record ShareLinkDto(
        string Id,
        int Document,
        string? Titre,
        DateTimeOffset CreeLe,
        DateTimeOffset ExpireLe,
        int? TelechargementsMax,
        int Telechargements);

    public sealed record ShareList(IReadOnlyList<ShareLinkDto> Results);

    /// <summary>Nombre maximal d'entrées du journal renvoyées (les plus récentes).</summary>
    public const int MaxLogEntries = 200;

    public sealed record AccessLogEntry(DateTimeOffset Date, string? Ip, string? Navigateur, bool Succes);

    public sealed record AccessLogList(IReadOnlyList<AccessLogEntry> Results);

    public static RouteGroupBuilder MapShares(this RouteGroupBuilder api)
    {
        api.MapGet("/partages/reglages", (IOptions<ShareOptions> options) =>
            TypedResults.Ok(new ShareSettings(options.Value.MaxExpirationDays)));
        api.MapGet("/partages", ListAsync);
        api.MapPost("/partages", CreateAsync);
        api.MapDelete("/partages/{id}", RevokeAsync);
        api.MapGet("/partages/{id}/journal", AccessLogAsync);
        return api;
    }

    private static int UserId(HttpContext context) => context.Features.Get<CurrentSession>()!.Session.UserId;

    /// <summary>
    /// Crée un lien après avoir vérifié, avec le token de l'utilisateur, qu'il voit le document dans Paperless.
    /// Le mot de passe n'est conservé que sous forme d'empreinte Argon2id.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateShareRequest? body,
        HttpContext context,
        PaperlessRelay relay,
        IShareLinkStore store,
        IOptions<ShareOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var maxDays = options.Value.MaxExpirationDays;
        if (ReadInt(body?.Document) is not { } documentId || documentId <= 0)
        {
            return Invalid("Document invalide.");
        }

        var password = body!.MotDePasse ?? "";
        if (password.Length < SharePasswordHasher.MinLength)
        {
            return Invalid($"Le mot de passe doit contenir au moins {SharePasswordHasher.MinLength} caractères.");
        }

        if (password.Length > SharePasswordHasher.MaxLength)
        {
            return Invalid($"Le mot de passe ne doit pas dépasser {SharePasswordHasher.MaxLength} caractères.");
        }

        if (ReadInt(body.Jours) is not { } days || days < 1 || days > maxDays)
        {
            return Invalid($"La durée de validité doit être comprise entre 1 et {maxDays} jours.");
        }

        int? maxDownloads = null;
        if (body.TelechargementsMax is not null)
        {
            if (ReadInt(body.TelechargementsMax) is not { } limit || limit < 1 || limit > MaxDownloadsLimit)
            {
                return Invalid($"Le nombre de téléchargements doit être compris entre 1 et {MaxDownloadsLimit}.");
            }

            maxDownloads = limit;
        }

        // Droit de lecture vérifié par Paperless lui-même : 404 si l'utilisateur ne voit pas le document.
        var denied = await relay.SendAsync(
            context, HttpMethod.Get, string.Create(CultureInfo.InvariantCulture, $"api/documents/{documentId}/?fields=id"), null,
            (_, _) => Task.FromResult<IResult?>(null),
            cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var now = time.GetUtcNow();
        var link = new ShareLink
        {
            Id = ShareLinkIds.New(),
            DocumentId = documentId,
            OwnerId = UserId(context),
            PasswordHash = SharePasswordHasher.Hash(password),
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
            MaxDownloads = maxDownloads,
        };
        await store.AddAsync(link, cancellationToken);

        return TypedResults.Created($"/api/partages/{link.Id}", ToDto(link, null));
    }

    /// <summary>Liens actifs de l'utilisateur, avec le titre actuel des documents (lu dans Paperless avec son token).</summary>
    private static async Task<IResult> ListAsync(
        HttpContext context, PaperlessRelay relay, IShareLinkStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var links = await store.ListActiveAsync(UserId(context), time.GetUtcNow(), cancellationToken);
        if (links.Count == 0)
        {
            return TypedResults.Ok(new ShareList([]));
        }

        var ids = links.Select(l => l.DocumentId).Distinct().ToList();
        var query = QueryString.Create(new KeyValuePair<string, string?>[]
        {
            new("id__in", string.Join(',', ids.Select(id => id.ToString(CultureInfo.InvariantCulture)))),
            new("fields", "id,title"),
            new("page_size", ids.Count.ToString(CultureInfo.InvariantCulture)),
        });

        var titles = new Dictionary<int, string>();
        var failed = await relay.SendAsync(
            context, HttpMethod.Get, "api/documents/" + query.ToUriComponent(), null,
            async (response, ct) =>
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
                foreach (var document in (body?["results"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (ReadInt(document["id"]) is { } id && document["title"] is JsonValue t && t.TryGetValue<string>(out var title))
                    {
                        titles[id] = title;
                    }
                }

                return null;
            },
            cancellationToken);
        if (failed is not null)
        {
            return failed;
        }

        // Document supprimé ou retiré à l'utilisateur depuis : le lien reste listé (pour le révoquer), sans titre.
        return TypedResults.Ok(new ShareList(
            [.. links.Select(l => ToDto(l, titles.TryGetValue(l.DocumentId, out var title) ? title : null))]));
    }

    private static async Task<IResult> RevokeAsync(
        string id, HttpContext context, IShareLinkStore store, TimeProvider time, CancellationToken cancellationToken) =>
        ShareLinkIds.IsValid(id) && await store.RevokeAsync(id, UserId(context), time.GetUtcNow(), cancellationToken)
            ? TypedResults.NoContent()
            : PaperlessRelay.Problem(HttpStatusCode.NotFound, "Lien introuvable.", "introuvable");

    /// <summary>Journal d'accès d'un lien, visible de son seul propriétaire.</summary>
    private static async Task<IResult> AccessLogAsync(
        string id, HttpContext context, IShareLinkStore store, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var entries = ShareLinkIds.IsValid(id)
            ? await store.ListAccessLogAsync(id, UserId(context), MaxLogEntries, cancellationToken)
            : null;
        return entries is null
            ? PaperlessRelay.Problem(HttpStatusCode.NotFound, "Lien introuvable.", "introuvable")
            : TypedResults.Ok(new AccessLogList([.. entries.Select(e => new AccessLogEntry(e.Timestamp, e.Ip, e.UserAgent, e.Success))]));
    }

    private static ShareLinkDto ToDto(ShareLink link, string? title) => new(
        link.Id, link.DocumentId, title, link.CreatedAt, link.ExpiresAt, link.MaxDownloads, link.DownloadCount);

    private static int? ReadInt(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<int>(out var value) ? value : null;

    private static IResult Invalid(string message) =>
        PaperlessRelay.Problem(HttpStatusCode.BadRequest, message, "requete_invalide");
}
