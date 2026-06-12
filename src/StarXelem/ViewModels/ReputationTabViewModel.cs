using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public partial class ReputationTabViewModel : PageViewModelBase
{
    private readonly IReputationService _reputationService;
    private List<ContractorModel> _allContractors = new();

    public override string Name => "Reputations";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Shield);

    
    
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<ContractorModel> FilteredContractors { get; } = new();

    public ReputationTabViewModel(IReputationService reputationService)
    {
        _reputationService = reputationService;
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterContractors();
    }

    protected override async Task OnFirstShowAsync()
    {
        await LoadDataCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadData()
    {
        IsLoading = true;
        try
        {
            var contractors = await _reputationService.GetSynchronizedReputationsAsync();
            _allContractors = contractors ?? new List<ContractorModel>();
            FilterContractors();
        }
        catch (Exception ex)
        {
            // In a real app, we would use the popup system to show an error
            Console.WriteLine($"Error loading reputations: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterContractors()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allContractors
            : _allContractors.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredContractors.Clear();
        foreach (var contractor in filtered)
        {
            FilteredContractors.Add(contractor);
        }
    }
}
