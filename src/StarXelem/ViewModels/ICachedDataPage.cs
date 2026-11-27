namespace StarXelem.ViewModels;

/// <summary>
/// Écran capable de réafficher les données gRPC de son dernier chargement, enregistrées par <see cref="Services.IGrpcDataCache"/>.
/// </summary>
public interface ICachedDataPage
{
    /// <summary>
    /// Réaffiche les données du dernier chargement enregistré, s'il y en a un, sans interroger le jeu. Sans effet si des données
    /// ont déjà été chargées depuis le jeu pendant cette session. Appelée au démarrage, une fois la base locale prête.
    /// </summary>
    Task RestoreCachedDataAsync();
}
