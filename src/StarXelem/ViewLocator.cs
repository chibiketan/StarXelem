using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using StarXelem.ViewModels;

namespace StarXelem;

public class ViewLocator : IDataTemplate
{
    private static bool _registered = false;
    private static readonly Dictionary<Type, Func<Control>> Registration = new();

    public static void RegisterViews()
    {
        // Register all views only once
        if (_registered)
            return;
        
        var viewModelType = typeof(ViewModelBase);
        var controlType = typeof(Control);

        var viewModelTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => viewModelType.IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var viewModel in viewModelTypes)
        {
            var viewName = viewModel.FullName?.Replace("ViewModel", "View");
            var view = viewName != null ? Type.GetType(viewName) : null;

            if (view != null && controlType.IsAssignableFrom(view))
            {
                var instance = Activator.CreateInstance(view);
                if (instance is not Control control)
                    throw new InvalidOperationException($"View {viewName} does not inherit from Control.");

                Registration.Add(viewModel, () => control);
            }
        }
        
        _registered = true;
    }

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}