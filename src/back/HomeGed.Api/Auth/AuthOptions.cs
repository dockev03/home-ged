using System.ComponentModel.DataAnnotations;

namespace HomeGed.Api.Auth;

/// <summary>Sessions et limitation des tentatives de connexion (section « Auth »).</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Durée d'inactivité au-delà de laquelle la session expire.</summary>
    [Range(1, 365)]
    public int IdleTimeoutDays { get; set; } = 30;

    /// <summary>Durée de vie maximale d'une session, même utilisée chaque jour.</summary>
    [Range(1, 365)]
    public int AbsoluteLifetimeDays { get; set; } = 90;

    /// <summary>Nombre d'échecs qui déclenche le blocage (par IP et par identifiant).</summary>
    [Range(1, 100)]
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>Fenêtre de comptage des échecs, et durée du blocage.</summary>
    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}
