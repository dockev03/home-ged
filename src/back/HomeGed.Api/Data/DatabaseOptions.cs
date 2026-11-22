using System.ComponentModel.DataAnnotations;

namespace HomeGed.Api.Data;

/// <summary>Base PostgreSQL <c>homeged</c> (section « Database »).</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Chaîne de connexion Npgsql (rôle dédié, limité à la base homeged).</summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>Applique les migrations EF Core au démarrage.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}
