using Avalonia.Media;
using FluentIcons.Common;
using Sc.Game.Editor.Mightybridge.V1;

namespace StarXelem.ViewModels;

public abstract class PageViewModelBase : ViewModelBase
{
    public abstract string Name { get; }
    public abstract IVisualSourceViewModel Icon { get; }

    private Geometry? _navIcon;

    /// <summary>Tracé de l'icône du menu latéral : contour sur une grille de 24 px, repris des maquettes.</summary>
    protected virtual string NavIconData => "M21 8l-9-5-9 5v8l9 5 9-5V8zM3 8l9 5 9-5M12 13v8";

    /// <summary>Icône du menu latéral, créée au premier affichage (thread d'UI).</summary>
    public Geometry NavIcon => _navIcon ??= StreamGeometry.Parse(NavIconData);

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