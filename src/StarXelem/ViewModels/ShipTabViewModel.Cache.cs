using System.Globalization;

using Avalonia.Threading;
using Microsoft.Extensions.Logging;

using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

/// <summary>
/// Conservation de la flotte entre deux lancements : le dernier chargement est enregistré et réaffiché au démarrage, jeu fermé compris.
/// </summary>
public partial class ShipTabViewModel : ICachedDataPage
{
    private const string ShipsDatasetKey = "ships";

    // Compte des vaisseaux affichés : à la connexion d'un autre compte, des vaisseaux relus du cache sont retirés.
    private string? _displayedAccountKey;

    /// <summary>Date des vaisseaux affichés et provenance (jeu ou cache).</summary>
    public DataFreshnessViewModel Freshness { get; } = new();

    /// <summary>Libellé du bouton de chargement : « Actualiser » dès que des vaisseaux sont affichés.</summary>
    public string LoadButtonLabel => Freshness.HasData ? "Actualiser" : "Charger";

    /// <inheritdoc />
    public Task RestoreCachedDataAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(RestoreCachedShipsAsync);
    }

    private void InitializeCache()
    {
        Freshness.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DataFreshnessViewModel.HasData))
            {
                OnPropertyChanged(nameof(LoadButtonLabel));
            }
        };
    }

    private async Task RestoreCachedShipsAsync()
    {
        if (IsLoading || Freshness.IsUpToDate)
        {
            return;
        }

        var cached = await _dataCache.LoadAsync(ShipsDatasetKey);
        if (cached == null || IsLoading || Freshness.IsUpToDate)
        {
            return;
        }

        IsLoading = true;
        TreatmentStatus = "Chargement des vaisseaux enregistrés…";
        try
        {
            var ships = await Task.Run(() => GrpcCachePayload.ReadShips(cached.Data));
            await ApplyShipClassesAsync(ships);

            _displayedAccountKey = cached.AccountKey;
            Freshness.Update(cached.FetchedAt, isFromCache: true);
            Spaceships = Task.FromResult<IList<SpaceshipModel>>(ships);
            SendFleetToOrbitalAllianceCommand.NotifyCanExecuteChanged();
            _logger.LogInformation("{Count} vaisseaux relus du cache (récupérés le {FetchedAt})", ships.Count, cached.FetchedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de réafficher les vaisseaux enregistrés");
        }
        finally
        {
            IsLoading = false;
            TreatmentStatus = string.Empty;
        }
    }

    // Nom et nom technique de chaque vaisseau, lus dans la base locale à partir de la classe de son droit.
    private async Task ApplyShipClassesAsync(IEnumerable<SpaceshipModel> ships)
    {
        foreach (var spaceship in ships)
        {
            var guid = spaceship.Entitlement.EntityClassGuid;
            var shipEntity = await _localDatabaseService.GetShipByGuidAsync(guid);

            if (shipEntity != null)
            {
                spaceship.ClassRecordName = shipEntity.TechnicalName;
                spaceship.Shipname = shipEntity.LocalizedName;
            }
            else
            {
                spaceship.ClassRecordName = guid;
                spaceship.Shipname = guid;
            }
        }
    }

    // La sérialisation se fait hors du thread d'UI ; une erreur est journalisée sans interrompre l'écran.
    private async Task SaveShipsToCacheAsync(IList<SpaceshipModel> ships, DateTimeOffset fetchedAt)
    {
        try
        {
            var payload = await Task.Run(() => GrpcCachePayload.WriteShips(ships)).ConfigureAwait(false);
            await _dataCache.SaveAsync(ShipsDatasetKey, payload, fetchedAt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'enregistrer les vaisseaux dans le cache");
        }
    }

    private void OnConnectionStatusChanged(GrpcConnectionStatus status)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnConnectionStatusChanged(status));
            return;
        }

        LoadShipListCommand.NotifyCanExecuteChanged();
        if (status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame)
        {
            ClearCachedShipsOfAnotherAccount();
        }
    }

    // À la connexion du jeu, des vaisseaux relus du cache pour un autre compte n'ont plus lieu d'être affichés.
    private void ClearCachedShipsOfAnotherAccount()
    {
        var connectedAccount = _clientService.CurrentPlayerGeid?.ToString(CultureInfo.InvariantCulture);
        if (!Freshness.IsFromCache || connectedAccount == null || connectedAccount == _displayedAccountKey)
        {
            return;
        }

        _logger.LogInformation("Vaisseaux enregistrés d'un autre compte retirés de l'affichage");
        _displayedAccountKey = null;
        LoadoutDetail = null;
        Freshness.Clear();
        Spaceships = null;
        SendFleetToOrbitalAllianceCommand.NotifyCanExecuteChanged();
    }
}
