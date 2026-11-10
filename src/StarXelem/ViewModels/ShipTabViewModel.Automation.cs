using System.IO;
using System.Text.Json;

using Avalonia.Threading;

namespace StarXelem.ViewModels;

public partial class ShipTabViewModel
{
    private static readonly TimeSpan AutomationConnectionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lance un chargement complet de la flotte sans intervention (option <c>--load-ships</c>).
    /// À appeler depuis le thread d'UI.
    /// </summary>
    /// <param name="reportPath">Fichier JSON où écrire le détail des vaisseaux, ou <c>null</c> pour ne rien écrire.</param>
    /// <returns><c>true</c> si le chargement a eu lieu, <c>false</c> si la connexion au jeu n'est pas disponible.</returns>
    public async Task<bool> RunAutomatedLoadAsync(string? reportPath)
    {
        Dispatcher.UIThread.VerifyAccess();

        var deadline = DateTime.UtcNow + AutomationConnectionTimeout;
        while (!LoadShipListCommand.CanExecute(null))
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(500);
        }

        await LoadShipListCommand.ExecuteAsync(null);

        if (reportPath != null && Spaceships != null)
        {
            await WriteShipsReportAsync(await Spaceships, reportPath);
        }

        return true;
    }

    private static async Task WriteShipsReportAsync(IEnumerable<Models.SpaceshipModel> ships, string path)
    {
        var rows = ships.Select(ship => new
        {
            ship.Shipname,
            State = ship.State.ToString(),
            Raw = ship.Location,
            Kind = ship.ResolvedLocation?.Kind.ToString(),
            ship.ResolvedLocation?.Id,
            ship.ResolvedLocation?.Name,
            ship.IsPorte,
            ship.IsLocationTypeLocation,
            ship.IsLocationTypeHangar,
            ship.IsLocationTypeId,
            ship.IsLocationTypeOther,
            ship.IsEmptyLocation
        }).ToList();

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, rows);
    }
}
