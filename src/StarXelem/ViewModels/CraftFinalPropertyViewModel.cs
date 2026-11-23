namespace StarXelem.ViewModels;

/// <summary>
/// Propriété finale de l'objet fabriqué : somme des modificateurs de tous les composants qui la portent.
/// </summary>
/// <param name="Name">Nom de la propriété.</param>
/// <param name="ContributionsLabel">Contribution de chaque composant : « Frame −9,8 % · Stock −12,8 % ».</param>
/// <param name="ResultLabel">Résultat : « 77,4% », « +1 », ou « incomplet ».</param>
/// <param name="ComponentCount">Nombre de composants qui portent la propriété.</param>
/// <param name="IsComplete">Faux si un composant n'a pas de qualité connue.</param>
public sealed record CraftFinalPropertyViewModel(string Name, string ContributionsLabel, string ResultLabel, int ComponentCount, bool IsComplete)
{
    /// <summary>Vrai si plusieurs composants portent la propriété : affiche le badge de cumul.</summary>
    public bool IsCumulative => ComponentCount > 1;

    /// <summary>Badge de cumul : « 2 composants ».</summary>
    public string ComponentCountLabel => $"{ComponentCount} composants";
}
