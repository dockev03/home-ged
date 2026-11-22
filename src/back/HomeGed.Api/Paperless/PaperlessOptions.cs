using System.ComponentModel.DataAnnotations;

namespace HomeGed.Api.Paperless;

/// <summary>Connexion à l'API REST de Paperless-ngx (section « Paperless »).</summary>
public sealed class PaperlessOptions
{
    public const string Section = "Paperless";

    /// <summary>URL interne de Paperless, sans slash final (ex. http://webserver:8000).</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "";

    /// <summary>Version d'API figée, envoyée dans l'en-tête Accept (voir docs/poc/auth-2fa.md).</summary>
    [Range(1, 100)]
    public int ApiVersion { get; set; } = 10;

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Délai d'un envoi de fichier (nouvelle version, upload) : transfert depuis un mobile compris.</summary>
    [Range(1, 240)]
    public int UploadTimeoutMinutes { get; set; } = 30;
}
