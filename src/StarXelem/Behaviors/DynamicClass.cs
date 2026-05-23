using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace StarXelem.Behaviors;

public static class DynamicClass
{
    public static readonly AttachedProperty<string?> NamesProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, string?>(
            "Names", typeof(DynamicClass));

    public static void SetNames(StyledElement element, string? value)
        => element.SetValue(NamesProperty, value);

    public static string? GetNames(StyledElement element)
        => element.GetValue(NamesProperty);

    static DynamicClass()
    {
        NamesProperty.Changed.AddClassHandler<StyledElement, String?>((element, e) =>
        {
            var oldClasses = Parse(e.OldValue.GetValueOrDefault<string?>());
            var newClasses = Parse(e.NewValue.GetValueOrDefault<string?>());

            // Retirer uniquement les classes qui ne sont plus présentes
            foreach (var cls in oldClasses.Except(newClasses))
                element.Classes.Remove(cls);

            // Ajouter uniquement les nouvelles classes (en évitant les doublons)
            foreach (var cls in newClasses.Except(oldClasses))
            {
                if (!element.Classes.Contains(cls))
                    element.Classes.Add(cls);
            }
        });
    }

    private static string[] Parse(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}