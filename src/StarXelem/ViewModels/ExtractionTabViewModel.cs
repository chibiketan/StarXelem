using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarXelem.Services;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;

namespace StarXelem.ViewModels;

public partial class ExtractionTabViewModel : PageViewModelBase
{
    private const string DataCorePath = @"Data\Game2.dcb";
    private readonly IP4kService _p4kService;
    private readonly ILogger<ExtractionTabViewModel> _logger;

    public override string Name => "Extractions";
    public override string Icon => nameof(Symbol.Download);

    [ObservableProperty] private bool _isExtracting = false;
    [ObservableProperty] private string _statusMessage = "";

    public ExtractionTabViewModel(IP4kService p4kService, ILogger<ExtractionTabViewModel> logger)
    {
        _p4kService = p4kService;
        _logger = logger;
    }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractCigIdsAsync()
    {
        if (_p4kService.SelectedP4KFile == null) return;

        var storageProvider = App.StorageProvider;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer les IDs CIG",
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Fichiers CSV") { Patterns = new[] { "*.csv" } }
            },
            SuggestedFileName = "cig_ids_crc32.csv"
        });

        if (file == null) return;

        IsExtracting = true;
        StatusMessage = "Extraction en cours...";

        try
        {
            await Task.Run(async () =>
            {
                await _p4kService.OpenP4k(_p4kService.SelectedP4KFile.Path, new Progress<double>(), new Progress<double>());
                
                using var entry = _p4kService.P4KFileSystem.OpenRead(DataCorePath);
                var dcb = new DataCoreDatabase(entry);
                
                var records = dcb.MainRecords;
                
                var csvContent = new StringBuilder();
                csvContent.AppendLine("CIG ID,CRC32");

                foreach (var guid in records)
                {
                    var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([guid]));
                    csvContent.AppendLine($"{guid},{crc}");
                }

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(csvContent.ToString());
            });

            StatusMessage = "Extraction terminée avec succès.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'extraction des IDs CIG");
            StatusMessage = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsExtracting = false;
        }
    }

    private bool CanExtract() => !IsExtracting && _p4kService.SelectedP4KFile != null;

    // Mise à jour de l'état du bouton quand le fichier p4k change
    public void OnSelectedP4KFileChanged()
    {
        ExtractCigIdsCommand.NotifyCanExecuteChanged();
    }
}
