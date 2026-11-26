using System.Globalization;

using Avalonia.Threading;
using Microsoft.Extensions.Logging;

using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public partial class ItemsTabViewModel
{
    private const string ItemsDatasetKey = "items";
    private const string LocationsDatasetKey = "items.locations";

    /// <summary>
    /// Réaffiche les objets du dernier chargement enregistré, s'il y en a un, sans interroger le jeu. Sans effet si des objets
    /// ont déjà été chargés depuis le jeu pendant cette session. À appeler une fois la base locale prête.
    /// </summary>
    public Task RestoreCachedItemsAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(RestoreCachedItemsCoreAsync);
    }

    private async Task RestoreCachedItemsCoreAsync()
    {
        if (IsLoading || Freshness.IsUpToDate)
        {
            return;
        }

        var cached = await _dataCache.LoadAsync(ItemsDatasetKey);
        if (cached == null || IsLoading || Freshness.IsUpToDate)
        {
            return;
        }

        IsLoading = true;
        TreatmentStatus = "Chargement des objets enregistrés…";
        try
        {
            var itemList = await Task.Run(() => GrpcCachePayload.ReadItems(cached.Data));
            var locations = await LoadCachedLocationsAsync(cached.FetchedAt);

            _locationResolutionCts?.Cancel();
            _resolvedLocations.Clear();
            foreach (var (id, location) in locations ?? new Dictionary<ulong, ResolvedLocation>())
            {
                _resolvedLocations[id] = location;
            }

            var result = await BuildRowsAsync(itemList, locations);
            ConfigureColumnFilters(result);

            _itemsFetchedAt = cached.FetchedAt;
            _itemsLoadedAt = cached.FetchedAt.LocalDateTime;
            _displayedAccountKey = cached.AccountKey;
            Freshness.Update(cached.FetchedAt, isFromCache: true);
            _unfilteredItemList = Task.FromResult<IList<ItemViewModel>>(result);
            IsLoading = false;

            if (locations != null)
            {
                // Applique les emplacements, alimente le filtre de colonne et publie les matériaux pour l'écran Blueprints.
                await RefreshLocationResultsAsync(result, isComplete: true);
            }
            else
            {
                PublishOwnedMaterials(result, _itemsLoadedAt, areLocationsResolved: false);
                await ScheduleFilters(TimeSpan.Zero);
            }

            _logger.LogInformation("{Count} objets relus du cache (récupérés le {FetchedAt})", result.Count, cached.FetchedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de réafficher les objets enregistrés");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<Dictionary<ulong, ResolvedLocation>?> LoadCachedLocationsAsync(DateTimeOffset itemsFetchedAt)
    {
        var cached = await _dataCache.LoadAsync(LocationsDatasetKey);
        if (cached == null)
        {
            return null;
        }

        var payload = await Task.Run(() => GrpcCachePayload.ReadJson<CachedItemLocations>(cached.Data));

        // Des emplacements d'un autre chargement (résolution interrompue) ne correspondraient pas aux objets relus.
        return payload != null && payload.ItemsFetchedAt == itemsFetchedAt ? payload.Locations : null;
    }

    // Construit les lignes du tableau ; un emplacement connu évite toute requête serveur pour l'objet.
    private async Task<List<ItemViewModel>> BuildRowsAsync(IList<EntityItemQueryResult> itemList, IReadOnlyDictionary<ulong, ResolvedLocation>? knownLocations)
    {
        var result = new List<ItemViewModel>(itemList.Count);
        foreach (var item in itemList)
        {
            var scItem = await _scItemRepository.GetByCrc32Async(item.EntityNodeProperties!.ClassGuidCrc);
            var knownLocation = knownLocations != null && knownLocations.TryGetValue(item.EntityNodeProperties.Geid, out var location) ? location : null;
            result.Add(new ItemViewModel(_localeEntryRepository, _locationService, _clientService, this, item, scItem, knownLocation));
        }

        return result;
    }

    // La sérialisation se fait hors du thread d'UI ; une erreur est journalisée par le cache sans interrompre l'écran.
    private async Task SaveToCacheAsync(string datasetKey, Func<byte[]> serialize, DateTimeOffset fetchedAt)
    {
        try
        {
            var payload = await Task.Run(serialize).ConfigureAwait(false);
            await _dataCache.SaveAsync(datasetKey, payload, fetchedAt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'enregistrer {Dataset} dans le cache", datasetKey);
        }
    }

    // À la connexion du jeu, des objets relus du cache pour un autre compte n'ont plus lieu d'être affichés.
    private void ClearCachedItemsOfAnotherAccount()
    {
        var connectedAccount = _clientService.CurrentPlayerGeid?.ToString(CultureInfo.InvariantCulture);
        if (!Freshness.IsFromCache || connectedAccount == null || connectedAccount == _displayedAccountKey)
        {
            return;
        }

        _logger.LogInformation("Objets enregistrés d'un autre compte retirés de l'affichage");
        _locationResolutionCts?.Cancel();
        _resolvedLocations.Clear();
        _displayedAccountKey = null;
        Freshness.Clear();
        _ownedMaterialService.Clear();
        _unfilteredItemList = Task.FromResult<IList<ItemViewModel>>(new List<ItemViewModel>());
        ScheduleFilters(TimeSpan.Zero);
    }
}
