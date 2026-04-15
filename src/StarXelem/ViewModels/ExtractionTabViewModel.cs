using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarXelem.Services;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace StarXelem.ViewModels;

public partial class ExtractionTabViewModel : PageViewModelBase
{
    private const string DataCorePath = @"Data\Game2.dcb";
    private readonly IP4kService _p4kService;
    private readonly ILogger<ExtractionTabViewModel> _logger;

    public override string Name => "Extractions";
    public override string Icon => nameof(Symbol.Download);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExtracting))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCigIdsCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateLocalisationCommand))]
    private bool _isExtractingCsv = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExtracting))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCigIdsCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateLocalisationCommand))]
    private bool _isExtractingLang = false;

    public bool IsExtracting => IsExtractingCsv || IsExtractingLang;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private double _csvProgress = 0;
    [ObservableProperty] private double _langProgress = 0;

    public ExtractionTabViewModel(IP4kService p4kService, ILogger<ExtractionTabViewModel> logger)
    {
        _p4kService = p4kService;
        _logger = logger;
        
        _p4kService.SelectedP4KFileChanged += (sender, model) => OnSelectedP4KFileChanged();
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
        }).ConfigureAwait(false);

        if (file == null) return;

        await Dispatcher.UIThread.InvokeAsync(() => IsExtractingCsv = true);

        try
        {
            await Task.Run(async () =>
            {
                await _p4kService.OpenP4k(_p4kService.SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);

                using var entry = _p4kService.P4KFileSystem.OpenRead(DataCorePath);
                var dcb = new DataCoreDatabase(entry);
                var records = dcb.MainRecords;

                await using var stream = await file.OpenWriteAsync().ConfigureAwait(false);
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteLineAsync("CIG ID,CRC32").ConfigureAwait(false);

                var total = records.Count;
                var i = 0;
                foreach (var guid in records)
                {
                    i++;
                    if (i % 50 == 0)
                    {
                        var pct = (double)i / total * 100;
                        await Dispatcher.UIThread.InvokeAsync(() => CsvProgress = pct);
                    }
                    var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([guid]));
                    await writer.WriteLineAsync($"{guid},{crc}").ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'extraction des IDs CIG");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => { IsExtractingCsv = false; CsvProgress = 0; });
        }
    }

    private bool CanExtract() => !IsExtracting && _p4kService.SelectedP4KFile != null;

    [RelayCommand(CanExecute = nameof(CanUpdateLocalisation))]
    private async Task UpdateLocalisationAsync()
    {
        EItemType[] componentTypeList = new[] { EItemType.Cooler, EItemType.QuantumDrive, EItemType.PowerPlant, EItemType.Shield };
        var classNameMap = new Dictionary<string, string>
        {
            {"Military", "M"},
            {"Civilian", "C"},
            {"Industrial", "I"},
            {"Stealth", "S"},
            {"Competition", "R"},
        };
        var gradeMap = new Dictionary<int, string>()
        {
            { 1, "A" },
            { 2, "B" },
            { 3, "C" },
            { 4, "D" },
            { 5, "E" },
        };
        
        var replacementMap = new Dictionary<string, string>(100);
        
        IsExtractingLang = true;
        try
        {
            // search for all components
            UpdateStatusMessage("Chargement du fichier p4k...");
            await _p4kService.OpenP4k(_p4kService.SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => LangProgress = 25);
            var entityDefinitionList = _p4kService.GetAllEntityClassDefinition(0);

            // Pour chaque définition d'entité existante
            await foreach (var entityDefinition in entityDefinitionList.ConfigureAwait(false))
            {
                // Normalement ce ne sont que des EntityClassDefinition
                var entityType = entityDefinition.Data as EntityClassDefinition;
                var attachableParams = entityType.Components.OfType<SAttachableComponentParams>().FirstOrDefault();

                // Si pas attachable ce n'est pas un composant, on continue
                if (null == attachableParams)
                {
                    continue;
                }

                if (!componentTypeList.Contains(attachableParams.AttachDef.Type))
                {
                    // Si ce n'est pas un composant de type cooler, quantum drive, power plant ou shield, on continue
                    continue;
                }

                var localisationKey = attachableParams.AttachDef.Localization.Name;

                if (localisationKey == "@LOC_PLACEHOLDER")
                {
                    // @LOC_PLACEHOLDER => Si c'est un placeholder, on doit ignorer car on ne peut rien faire
                    _logger.LogWarning("Composant de type {0} ne comportant pas de nom !", attachableParams.AttachDef.Type);
                    continue;
                }

                var grade = gradeMap[attachableParams.AttachDef.Grade];
                var size = attachableParams.AttachDef.Size;
                var className = "?";
                // récupérer la classe (faut regarder dans la description...)
                var description = await _p4kService.GetLocaleValue(attachableParams.AttachDef.Localization.Description).ConfigureAwait(false);
                var searchRegex = new Regex(@"Class:\s*(\w+)");
                // On retire le '@' devant la description
                var searchResult = searchRegex.Match(description.Substring(1));
                if (searchResult.Success)
                {
                    // On a trouvé la classe, on l'extrait
                    className = classNameMap[searchResult.Groups[1].Value];
                }

                if (!replacementMap.TryAdd(localisationKey.Substring(1), $"{className}{size}{grade}"))
                {
                    _logger.LogWarning("Impossible d'ajouter la clé {0} dans le tableau car elle existe déjà. nouvelle valeur : '{1}', ancienne valeur : {2}", localisationKey.Substring(1), $"{className}{size}{grade}", replacementMap[localisationKey.Substring(1)]);
                }
            }
            
            await Dispatcher.UIThread.InvokeAsync(() => LangProgress = 60);

            // extract localisation file, check each line then write the final file
            UpdateStatusMessage("Ecriture du fichier de localisation en cours...");
            await using var rawStream = _p4kService.P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
            using var textReader = new StreamReader(rawStream, Encoding.UTF8);

            var localisationFilePath = Path.Combine(Path.GetDirectoryName(_p4kService.SelectedP4KFile.Path)!, @"data/Localization/english/global.ini");
            var file = new FileInfo(localisationFilePath);

            if (!file.Exists)
            {
                // Le fichier n'existe pas déjà
                // Ensure directory exists
                file.Directory!.Create();
            }

            using var fileStream = file.OpenWrite();
            using var fileWriter = new StreamWriter(fileStream, Encoding.UTF8);
            
            string? line;
            while (null != (line = await textReader.ReadLineAsync()))
            {
                var split = line.Split('=', 2);

                if (replacementMap.TryGetValue(split[0], out var prefix))
                {
                    split[1] = $"{prefix} {split[1]}";
                }
                
                // On écrit dans le fichier final
                fileWriter.WriteLine(string.Join('=', split));
            }

            await Dispatcher.UIThread.InvokeAsync(() => LangProgress = 90);
            UpdateStatusMessage("Ecriture du fichier de localisation terminée !");
            await Dispatcher.UIThread.InvokeAsync(() => LangProgress = 100);
            await Task.Delay(4000).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Une erreur est survenue lors de la mise à jour du fichier de localisation");
            UpdateStatusMessage($"Une erreur est survenue : {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => { IsExtractingLang = false; LangProgress = 0; });
        }
    }
    
    private bool CanUpdateLocalisation() => !IsExtracting && _p4kService.SelectedP4KFile != null;

    private void UpdateStatusMessage(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusMessage = message;
        });
    }
    
    // Mise à jour de l'état des boutons quand le fichier p4k change
    public void OnSelectedP4KFileChanged()
    {
        ExtractCigIdsCommand.NotifyCanExecuteChanged();
        UpdateLocalisationCommand.NotifyCanExecuteChanged();
    }
}
