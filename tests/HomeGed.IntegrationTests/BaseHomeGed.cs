namespace HomeGed.IntegrationTests;

/// <summary>
/// Tests qui utilisent la base homeged : jamais en parallèle (le nettoyage des sessions par l'un
/// couperait la session de l'autre).
/// </summary>
[CollectionDefinition(Name)]
public sealed class BaseHomeGed
{
    public const string Name = "Base homeged";
}
