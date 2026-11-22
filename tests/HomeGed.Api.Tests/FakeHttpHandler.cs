using System.Net;

namespace HomeGed.Api.Tests;

/// <summary>Faux Paperless : renvoie la réponse fournie et mémorise la dernière requête (et son corps).</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => respond(request))
    {
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastBody { get; private set; }

    public int Calls { get; private set; }

    /// <summary>Toutes les requêtes reçues (méthode, chemin et requête, corps), dans l'ordre.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<(HttpMethod Method, string PathAndQuery, string? Body)> History { get; } = new();

    public static FakeHttpHandler Returning(HttpStatusCode status, params (string Name, string Value)[] headers) =>
        Returning(status, null, headers);

    public static FakeHttpHandler Returning(HttpStatusCode status, string? json, params (string Name, string Value)[] headers) =>
        new(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        History.Enqueue((request.Method, request.RequestUri!.PathAndQuery, LastBody));
        return respond(request, LastBody);
    }
}
