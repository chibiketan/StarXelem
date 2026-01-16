using Sc.Game.Editor.Mightybridge.V1;

namespace StarXelem.ViewModels;

public abstract class PageViewModelBase : ViewModelBase
{
    public abstract string Name { get; }
    public abstract string Icon { get; }
    
    public bool IsLoaded { get;private set; }
    
    public async Task LoadAsync()
    {
        if (!IsLoaded)
        {
            await OnFirstShowAsync();
            IsLoaded = true;
        }
        
        await OnShowAsync();
    }

    protected virtual Task OnFirstShowAsync()
    {
        return Task.CompletedTask;
    }
    
    protected virtual Task OnShowAsync()
    {
        return Task.CompletedTask;
    }

}