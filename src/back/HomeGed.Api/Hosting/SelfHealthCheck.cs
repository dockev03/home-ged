namespace HomeGed.Api.Hosting;

/// <summary>
/// `dotnet HomeGed.Api.dll --healthcheck` : interroge /health en local et renvoie 0 ou 1.
/// Utilisé par le HEALTHCHECK Docker (l'image runtime n'a ni curl ni wget).
/// </summary>
public static class SelfHealthCheck
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
