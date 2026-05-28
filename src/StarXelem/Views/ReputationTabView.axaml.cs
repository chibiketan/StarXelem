using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.Views
{
    public partial class ReputationTabView : UserControl
    {
        public ReputationTabView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Event handler called when a standings ItemsControl is loaded. Applies the
        /// current-standing and locked-standing CSS classes to each standing item
        /// based on the reputation scope's CurrentStanding and CurrentValue (§17.7).
        /// </summary>
        private void OnStandingsListLoaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl standingsList)
                return;

            ApplyStandingClasses(standingsList);
        }

        /// <summary>
        /// Applies the current-standing and locked-standing CSS classes to each standing item
        /// based on the reputation scope's CurrentStanding and CurrentValue (§17.7).
        /// </summary>
        private static void ApplyStandingClasses(ItemsControl standingsList)
        {
            var scopeData = standingsList.DataContext as ReputationModel;
            if (scopeData is null)
                return;

            foreach (var item in standingsList.Items)
            {
                if (item is not StandingModel standing)
                    continue;

                var container = standingsList.ContainerFromItem(item!);
                if (container is not Border border)
                    continue;
                if (standing is null)
                    continue;

                // Current standing: highlighted (§17.7)
                if (standing.Name == scopeData.CurrentStanding?.Name)
                {
                    border.Classes.Add("current-standing");
                }
                // Not yet reached: reduced opacity (§17.7)
                else if (scopeData.CurrentValue.HasValue && standing.Min > scopeData.CurrentValue.Value)
                {
                    border.Classes.Add("locked-standing");
                }
            }
        }
    }
}
