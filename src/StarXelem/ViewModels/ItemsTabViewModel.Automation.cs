using System.IO;
using System.Text.Json;

using Avalonia.Threading;
using Microsoft.Extensions.Logging;

using StarXelem.Models;

namespace StarXelem.ViewModels;

public partial class ItemsTabViewModel
{
    private static readonly TimeSpan AutomationConnectionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lance un chargement complet sans intervention (option <c>--load-items</c>) et attend la fin de la résolution
    /// des emplacements. À appeler depuis le thread d'UI.
    /// </summary>
    /// <param name="reportPath">Fichier JSON où écrire le détail des objets, ou <c>null</c> pour ne rien écrire.</param>
    public async Task RunAutomatedLoadAsync(string? reportPath)
    {
        Dispatcher.UIThread.VerifyAccess();

        var deadline = DateTime.UtcNow + AutomationConnectionTimeout;
        while (!CanLoadItemList())
        {
            if (DateTime.UtcNow > deadline)
            {
                _logger.LogWarning("Chargement automatique abandonné : pas de connexion au jeu après {Timeout}", AutomationConnectionTimeout);
                return;
            }

            await Task.Delay(500);
        }

        if (LocationList == null || await LocationList is not { Count: > 0 })
        {
            ReloadLocationList();
            await LocationList!;
        }

        await LoadItemListCommand.ExecuteAsync(null);

        if (_locationResolutionTask != null)
        {
            await _locationResolutionTask;
        }

        _logger.LogInformation("Chargement automatique terminé : {Summary}", ResultSummary);

        if (reportPath != null && _unfilteredItemList != null)
        {
            await WriteItemsReportAsync(await _unfilteredItemList, reportPath);
        }
    }

    private async Task WriteItemsReportAsync(IList<ItemViewModel> items, string path)
    {
        var itemsById = items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var rows = new List<object>(items.Count);

        foreach (var item in items)
        {
            var parentId = item.Edge?.End.HasEntityId == true ? item.Edge.End.EntityId : (ulong?)null;
            var parent = parentId is { } id ? itemsById.GetValueOrDefault(id) : null;

            rows.Add(new
            {
                item.Id,
                Name = await GetNameAsync(item),
                Type = item.ItemType.ToString(),
                SubType = item.ItemSubType.ToString(),
                TechnicalName = item.LocalTypeName,
                item.ClassGuidCrc,
                Location = await GetLocationKeyAsync(item),
                EdgeType = item.EdgeType?.ToString(),
                item.EdgeLocation,
                ParentId = parentId,
                ParentLoaded = parent != null,
                ParentClassGuidCrc = parent?.ClassGuidCrc,
                ParentTechnicalName = parent?.LocalTypeName,
                ParentType = parent?.ItemType.ToString()
            });
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, rows, new JsonSerializerOptions { WriteIndented = false });
        _logger.LogInformation("Rapport des objets écrit : {Path}", path);
    }
}
