using Avalonia;
using StarXelem.Models;

namespace StarXelem.Services.Scan;

public interface IOverlayNotificationService
{
    /// <summary>Affiche l'overlay de résultat sous la position écran du candidat, pendant <see cref="Constants.ScanConstants.OverlayDurationMs"/>.</summary>
    Task ShowAsync(SignatureMatch match);

    /// <summary>Affiche un overlay avec des lignes libres (ex. "Signature inconnue" + voisins) à une position écran donnée.</summary>
    Task ShowMessageAsync(IReadOnlyList<string> lines, PixelPoint at);
}
