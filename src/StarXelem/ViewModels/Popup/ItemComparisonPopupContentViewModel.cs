using CommunityToolkit.Mvvm.ComponentModel;
using StarXelem.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ClosedXML.Excel;
using StarXelem.Extensions;

namespace StarXelem.ViewModels.Popup;

public partial class ItemComparisonPopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private string _sourceFile;
    [ObservableProperty] private IReadOnlyList<ItemExcelModel> _source;
    [ObservableProperty] private IReadOnlyList<ItemViewModel> _target;
    [ObservableProperty] private IReadOnlyList<ItemTypeComparisonResult> _results = new List<ItemTypeComparisonResult>();
    [ObservableProperty] private IReadOnlyList<ItemTypeComparisonResult> _filteredResults = new List<ItemTypeComparisonResult>();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private IReadOnlyList<ComparisonDiffTypeOption> _comparisonDiffTypeOptions;
    [ObservableProperty] private string? _searchedName;

    public ItemComparisonPopupContentViewModel()
    {
        IsLoading = false;
        Source = new List<ItemExcelModel>();
        Target = new List<ItemViewModel>();
        SourceFile = "";
        ComparisonDiffTypeOptions = Enum.GetValues<ComparisonDiffType>().Select(type => new ComparisonDiffTypeOption(type)).ToList();
        _searchedName = "";

        foreach (var comparisonDiffTypeOption in ComparisonDiffTypeOptions)
        {
            comparisonDiffTypeOption.PropertyChanged += OnComparisonDiffTypeOptionPropertyChanged;
        }
    }
    
    public async Task OnPopupShownAsync()
    {
        IsLoading = true;
        // Load file
        await LoadFileAsync();
        // Recompute diff
        Recompute();
        IsLoading = false;
    }

    private async Task LoadFileAsync()
    {
        // Préparer la boîte de dialogue "Enregistrer sous"
        var suggestedName = $"Items_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var storageProvider = lifetime?.MainWindow?.StorageProvider;

        if (storageProvider == null)
        {
            // TODO Problème à remonter à l'utilisateur
            return;
        }

        var fileType = new FilePickerFileType("Classeur Excel")
        {
            Patterns = new[] { "*.xlsx" },
            AppleUniformTypeIdentifiers = new[] { "org.openxmlformats.spreadsheetml.sheet" },
            MimeTypes = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
        };

        var saveResult = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir le fichier source pour la comparaison",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { fileType }
        });

        if (saveResult.Count == 0)
        {
            // TODO comment indiquer un problème ?
            return;
        }

        // Selon la plateforme, TryGetLocalPath peut être null (ex: sandboxed). On utilisera le stream alors.
        var selectedPath = saveResult.First().TryGetLocalPath();
        List<ItemExcelModel> result = new List<ItemExcelModel>();

        using (var workbook = XLWorkbook.OpenFromTemplate(selectedPath))
        {
            var ws = workbook.Worksheet("Items");
            var headers = ws.Row(1)
                .Cells()
                .ToDictionary(x => x.Value.ToString(), x => x.Address.ColumnNumber);

            // Write rows
            var currentRow = ws.FirstRow().RowBelow();
            var lastRow = ws.LastRowUsed();

            while (currentRow != lastRow)
            {
                var item = new ItemExcelModel();
                item.TechnicalType = currentRow.Cell(headers["Type technique"]).Value.ToString();
                item.Name = currentRow.Cell(headers["Nom"]).Value.ToString();
                item.Count = currentRow.Cell(headers["Nombre élément"]).Value.TryConvert(out double val, CultureInfo.InvariantCulture) ? (int)val : 0;
                result.Add(item);
                currentRow = currentRow.RowBelow();
            }
        }
        
        Source = result;
    }

    private void Recompute()
    {
        var results = CompareByType(_source, _target);
        Results = results;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = Results.AsQueryable();
        // filtre par type
        var searchedTypes = _comparisonDiffTypeOptions.Where(o => o.IsSelected).Select(o => o.Type).ToList();
        // Si rien n'est sélectionné, on affiche tous les types
        if (searchedTypes.Count == 0)
        {
            searchedTypes = Enum.GetValues<ComparisonDiffType>().ToList();
        }
        
        query = query.Where(r => searchedTypes.Contains(r.Status));
        // filtre par nom/nom technique
        if (!String.IsNullOrWhiteSpace(SearchedName))
        {
            query = query.Where(r => (r.Name ?? "").Contains(SearchedName, StringComparison.OrdinalIgnoreCase) || r.TechnicalType.Contains(SearchedName, StringComparison.OrdinalIgnoreCase));
        }
        
        FilteredResults = query.ToList();
    }

    partial void OnSearchedNameChanged(string? value)
    {
        ApplyFilter();
    }

    void OnComparisonDiffTypeOptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ApplyFilter();
    }

    public static IReadOnlyList<ItemTypeComparisonResult> CompareByType(
        IReadOnlyList<ItemExcelModel>? source,
        IReadOnlyList<ItemViewModel>? target)
    {
        source ??= new List<ItemExcelModel>();
        target ??= new List<ItemViewModel>();

        // Group source by TechnicalType and sum Count
        var sourceMap = source
            .Where(s => !string.IsNullOrWhiteSpace(s.TechnicalType))
            .GroupBy(s => s.TechnicalType)
            .ToDictionary(g => g.Key, g => new {Sum = g.Sum(x => x.Count), Name = g.First().Name});

        // Group target by LocalTypeName and sum StackSize (null -> 1)
        var targetMap = target
            .Where(t => !string.IsNullOrWhiteSpace(t.LocalTypeName))
            .GroupBy(t => t.LocalTypeName!)
            .ToDictionary(g => g.Key, g => new {Sum = g.Sum(x => (int)(x.StackSize ?? 1)), Name = g.First().Name});

        var allKeys = new HashSet<string>(sourceMap.Keys);
        allKeys.UnionWith(targetMap.Keys);

        var list = new List<ItemTypeComparisonResult>(allKeys.Count);

        foreach (var key in allKeys.OrderBy(k => k))
        {
            var src = sourceMap.GetValueOrDefault(key);
            var tgt = targetMap.GetValueOrDefault(key);

            var status = GetStatus(src?.Sum ?? 0, tgt?.Sum ?? 0);

            list.Add(new ItemTypeComparisonResult
            {
                TechnicalType = key,
                Name = src?.Name ?? tgt?.Name.Result,
                SourceCountSum = src?.Sum ?? 0,
                TargetStackSum = tgt?.Sum ?? 0,
                Status = status
            });
        }

        return list;
    }

    private static ComparisonDiffType GetStatus(int sourceSum, int targetSum)
    {
        if (sourceSum == 0 && targetSum > 0) return ComparisonDiffType.OnlyTarget;
        if (targetSum == 0 && sourceSum > 0) return ComparisonDiffType.OnlySource;
        if (sourceSum == targetSum) return ComparisonDiffType.Equal;
        if (targetSum > sourceSum) return ComparisonDiffType.Gain;
        return ComparisonDiffType.Loss;
    }
    
    public partial class ComparisonDiffTypeOption : ObservableObject
    {
        public ComparisonDiffType Type { get; }
        [ObservableProperty] private bool _isSelected = true;
        public string DisplayName => Type.GetDisplayName() ?? Type.ToString();

        public ComparisonDiffTypeOption(ComparisonDiffType type)
        {
            Type = type;
        }
    }

}