namespace HomeGed.IntegrationTests;

/// <summary>Variables fournies par la CI (ou à la main) pour les tests contre des services réels.</summary>
internal static class IntegrationEnvironment
{
    public static string PaperlessUrl => Require("PAPERLESS_URL");

    /// <summary>Compte Paperless sans 2FA (PAPERLESS_ADMIN_USER de la CI).</summary>
    public static string PaperlessUser => Require("PAPERLESS_USER");

    public static string PaperlessPassword => Require("PAPERLESS_PASSWORD");

    /// <summary>Chaîne de connexion à une base PostgreSQL jetable (elle est vidée par les tests).</summary>
    public static string HomeGedDb => Require("HOMEGED_DB");

    private static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} non défini : ces tests demandent des services réels.");
}
