using Avalonia.Media;
using FluentIcons.Common;
using Sc.Game.Editor.Mightybridge.V1;

namespace StarXelem.ViewModels;

public abstract class PageViewModelBase : ViewModelBase
{
    public abstract string Name { get; }
    public abstract IVisualSourceViewModel Icon { get; }

    public bool IsLoaded { get;private set; }
    
    public async Task LoadAsync()
    {
        if (!IsLoaded)
        {
            await OnFirstShowAsync().ConfigureAwait(false);
            IsLoaded = true;
        }
        
        await OnShowAsync().ConfigureAwait(false);
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