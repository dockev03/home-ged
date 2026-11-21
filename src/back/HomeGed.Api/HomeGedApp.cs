using System.Net;
using System.Reflection;
using HomeGed.Api.Hosting;
using HomeGed.Api.Paperless;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace HomeGed.Api;

/// <summary>Construction de l'application, partagée par Program et les tests.</summary>
public static class HomeGedApp
{
    /// <summary>Limite d'upload alignée avec NPM et Paperless (A8).</summary>
    public const long MaxRequestBodyBytes = 200L * 1024 * 1024;

    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Logs structurés JSON sur stdout (§4 Exploitation).
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o =>
        {
            o.UseUtcTimestamp = true;
            o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });

        builder.Services.AddOptions<PaperlessOptions>()
            .Bind(builder.Configuration.GetSection(PaperlessOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddHttpClient<PaperlessClient>((sp, http) =>
            PaperlessClient.Configure(http, sp.GetRequiredService<IOptions<PaperlessOptions>>().Value))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // L'API ne redirige que vers l'interface web : on ne suit pas.
                AllowAutoRedirect = false,
                UseCookies = false,
            });

        builder.Services.AddHealthChecks()
            .AddCheck<PaperlessHealthCheck>("paperless", tags: ["ready"]);

        // Derrière Nginx Proxy Manager : IP client et schéma HTTPS d'origine.
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            foreach (var network in builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        builder.Services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

        configure?.Invoke(builder);

        var app = builder.Build();

        app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseSecurityHeaders();

        // Vivacité (Docker) : ne dépend de rien. Disponibilité : vérifie Paperless.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

        var api = app.MapGroup("/api");
        api.MapGet("/info", () => TypedResults.Ok(new AppInfo(AppVersion)));
        // Toute autre route /api/* : 404 JSON, jamais la page de l'application.
        api.Map("/{**rest}", () => Results.Problem(statusCode: (int)HttpStatusCode.NotFound));

        // Front Vue buildé (wwwroot) : fichiers fingerprintés en cache long, index.html jamais en cache.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                var path = ctx.Context.Request.Path;
                ctx.Context.Response.Headers.CacheControl =
                    path.StartsWithSegments("/assets", StringComparison.Ordinal)
                        ? "public, max-age=31536000, immutable"
                        : "no-cache";
            },
        });
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
        });

        return app;
    }

    public static string AppVersion { get; } =
        typeof(HomeGedApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}

public sealed record AppInfo(string Version);
