using System.Net;

namespace HomeGed.Api.Tests;

/// <summary>Faux Paperless : renvoie la réponse fournie et mémorise la dernière requête.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public static FakeHttpHandler Returning(HttpStatusCode status, params (string Name, string Value)[] headers) =>
        new(_ =>
        {
            var response = new HttpResponseMessage(status);
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(respond(request));
    }
}
