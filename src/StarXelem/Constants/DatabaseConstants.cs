namespace StarXelem.Constants;

/// <summary>
/// Numéro de version du format/contenu de la base de données locale.
/// À incrémenter à chaque modification du schéma de la base ou de la façon dont les données sont chargées,
/// afin de déclencher une reconstruction automatique de la base chez les utilisateurs existants.
/// </summary>
public static class DatabaseConstants
{
    public const int DatabaseVersion = 1;
}
