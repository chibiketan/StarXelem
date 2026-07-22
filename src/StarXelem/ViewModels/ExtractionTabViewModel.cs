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
    private readonly ILocalDatabaseService _localDatabaseService;
    private readonly IGrpcClientService _grpcClientService;
    private readonly ILogger<ExtractionTabViewModel> _logger;

    public override string Name => "Extractions";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.ArrowDownload);

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
    [ObservableProperty] private bool _includeBlueprints = true;
    [ObservableProperty] private bool _includeObtainedBlueprints = false;
    [ObservableProperty] private bool _isGrpcConnected = false;

    public ExtractionTabViewModel(IP4kService p4kService, ILocalDatabaseService localDatabaseService, IGrpcClientService grpcClientService, ILogger<ExtractionTabViewModel> logger)
    {
        _p4kService = p4kService;
        _localDatabaseService = localDatabaseService;
        _grpcClientService = grpcClientService;
        _logger = logger;
        
        _p4kService.SelectedP4KFileChanged += (sender, model) => OnSelectedP4KFileChanged();
        _grpcClientService.OnStatusChanged += OnGrpcStatusChanged;
        UpdateGrpcConnectedState();
    }

    private void OnGrpcStatusChanged(object? sender, GrpcConnectionStatus status)
    {
        UpdateGrpcConnectedState();
    }

    private void UpdateGrpcConnectedState()
    {
        var connected = _grpcClientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame;
        if (IsGrpcConnected != connected)
        {
            IsGrpcConnected = connected;
            if (!connected)
            {
                IncludeObtainedBlueprints = false;
            }
        }
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

            // Build mineral-to-signature maps from mineable entities
            UpdateStatusMessage("Extraction des signatures radar des minéraux...");
            var mineralSignatureMap = new Dictionary<string, int>(); // localized name -> signature
            var mineralSignatureMapLower = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // lowercase name -> signature
            var excludedWords = new []{"deposit", "ore", "raw", "items", "commodities", "r"};

            await foreach (var entityDefinition in _p4kService.GetAllEntityClassDefinition(0).ConfigureAwait(false))
            {
                var entityType = entityDefinition.Data as EntityClassDefinition;
                if (!(entityType?.Components.OfType<MineableParams>().Any() ?? false) || !entityType.Components.OfType<SSCSignatureSystemParams>().Any())
                    continue;

                var entityDefinitionWithDepth = await _p4kService.EnsureRecordsDepthAsync([entityDefinition], 3);
                entityType = (EntityClassDefinition)entityDefinitionWithDepth[0].Data;
                var mineableParams = entityType.Components.OfType<MineableParams>().First();
                var signatureParams = entityType.Components.OfType<SSCSignatureSystemParams>().First();
                // Extract radar signature (index 4 = mineral channel)
                // baseSignatureParams is typed as SSCSignatureParamsBase but the actual runtime type is SSCSignatureSystemBaseSignatureParams
                var baseSigParams = signatureParams.radarProperties?.baseSignatureParams as SSCSignatureSystemBaseSignatureParams;
                if (baseSigParams?.signatures == null || baseSigParams.signatures.Length < 5)
                    continue;

                var signatureValue = (int)Math.Round(baseSigParams.signatures[4]);

                // Skip generic signatures (FPS=3000, GroundVehicle=4000)
                if (signatureValue is 3000 or 4000)
                    continue;

                // Extract mineral names from composition
                if (mineableParams.composition?.compositionArray == null)
                    continue;


                foreach (var part in mineableParams.composition.compositionArray)
                {
                    if (part.mineableElement?.resourceType == null)
                        continue;

                    var resourceType = part.mineableElement.resourceType;
                    var displayName = resourceType.displayName;

                    if (string.IsNullOrEmpty(displayName))
                        continue;

                    // Get localized mineral name
                    var localizedName = await _p4kService.GetLocaleValue(displayName).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(localizedName))
                        continue;

                    // Add to maps if not already present (keep first/unique signature)
                    if (!mineralSignatureMap.ContainsKey(localizedName))
                    {
                        mineralSignatureMap[localizedName] = signatureValue;
                        var mineralKeyName = String.Concat(localizedName
                            .Split(" ", StringSplitOptions.RemoveEmptyEntries)
                            .Select(w => w.Trim('@', '(', ')').ToLowerInvariant())
                            .Where(w => !excludedWords.Contains(w)));
                
                        mineralSignatureMapLower[mineralKeyName.ToLowerInvariant()] = signatureValue;
                    }
                }
            }

            _logger.LogInformation("Found {Count} minerals with unique radar signatures", mineralSignatureMap.Count);

            await Dispatcher.UIThread.InvokeAsync(() => LangProgress = 60);

            // Query DB for missions with blueprint pools to build BP suffix maps
            Dictionary<string, string> titleSuffixMap = new();
            Dictionary<string, Dictionary<string, HashSet<string>>> descAppendMap = new();

            if (IncludeBlueprints)
            {
                UpdateStatusMessage("Chargement des récompenses BP depuis la base de données...");
                HashSet<string>? obtainedIds = null;

                if (IncludeObtainedBlueprints && IsGrpcConnected)
                {
                    UpdateStatusMessage("Chargement des BP obtenus depuis le jeu...");
                    var grpcBps = await _grpcClientService.GetBlueprintList().ConfigureAwait(false);
                    obtainedIds = grpcBps.Select(e => e.BlueprintId).ToHashSet();
                }

                (titleSuffixMap, descAppendMap) = await _localDatabaseService.GetBlueprintRewardMapsAsync(obtainedIds);
            }

            // extract localisation file, check each line then write the final file
            UpdateStatusMessage("Ecriture du fichier de localisation en cours...");
            await using var rawStream = _p4kService.P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
            using var textReader = new StreamReader(rawStream, Encoding.UTF8);

            var localisationFilePath = Path.Combine(Path.GetDirectoryName(_p4kService.SelectedP4KFile.Path)!, @"data/Localization/english/global.ini");
            var file = new FileInfo(localisationFilePath);

            if (!file.Exists)
            {
                file.Directory!.Create();
            }

            using var fileStream = file.OpenWrite();
            using var fileWriter = new StreamWriter(fileStream, Encoding.UTF8);
            
            string? line;
            while (null != (line = await textReader.ReadLineAsync()))
            {
                var split = line.Split('=', 2);
                var key = split[0];

                if (replacementMap.TryGetValue(key, out var prefix))
                {
                    split[1] = $"{prefix} {split[1]}";
                }

                if (titleSuffixMap.TryGetValue(key, out var suffix))
                {
                    split[1] = $"{split[1]} {suffix}";
                }

                if (descAppendMap.TryGetValue(key, out var poolMap))
                {
                    var sb = new StringBuilder(split[1]);
                    foreach (var (poolName, bpSet) in poolMap.OrderBy(p => p.Key))
                    {
                        sb.Append($"\\n\\n<EM3>**{poolName}**</EM3>");
                        foreach (var bpName in bpSet.OrderBy(n => n))
                        {
                            sb.Append($"\\n- {bpName}");
                        }
                    }
                    split[1] = sb.ToString();
                }

                if (key == "Journal_General_Mining_Compendium_Content")
                {
                    var val = "";
                    for (var i = 0; i <= 20; ++i)
                    {
                        val += $"<EM{i}>Un texte d'essai avec la balise EM{i}</EM{i}>\\n";
                    }
                    var content = split[1];
                    // Append radar signatures to mineral names in the compendium
                    foreach (var (mineralName, signature) in mineralSignatureMap)
                    {
                        // Use word boundary regex to match the mineral name and append signature
                        var escapedName = Regex.Escape(mineralName);
                        content = Regex.Replace(content, escapedName, $"{mineralName} (RS {signature})");
                    }
                    split[1] = $"Test des couleurs EM\\n\\n{val}\\n\\n\\n\\n{content}";
                }

                // Handle mineabletype_primary_* keys for mission objectives
                if (key.StartsWith("mineabletype_primary_"))
                {
                    var mineralKey = key["mineabletype_primary_".Length..].ToLowerInvariant();

                    if (mineralKey == "aluminium") mineralKey = "aluminum"; // why CIG, why ?
                    if (mineralKey == "savrillium") mineralKey = "savrilium"; // why CIG, why ?
                    if (mineralSignatureMapLower.TryGetValue(mineralKey, out var rsSignature))
                    {
                        split[1] = $"{split[1]} (RS {rsSignature})";
                    }
                }

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
