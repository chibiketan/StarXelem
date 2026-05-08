using Avalonia.Headless.XUnit;
using StarXelem.ViewModels;
using Avalonia.LogicalTree;
using StarXelem.Views;

namespace StarXelem.Tests.Visual;

public class DebugTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;
    private static List<string> _log = new();

    public DebugTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        File.WriteAllLines("debug_output.txt", _log);
    }

    [AvaloniaFact]
    public async Task Debug_GetActivePageContent_Flow()
    {
        _log.Clear();
        var L = (string msg) => _log.Add(msg);

        var window = await VisualPilot.OpenAppAsync(_fixture);
        VisualPilot.NavigateToTab(window, "Amis");

        var vm = (MainWindowViewModel)window.DataContext!;
        var currentPageType = vm.CurrentPage!.GetType();
        L($"CurrentPage type: {currentPageType.FullName}");

        var viewName = currentPageType.FullName?.Replace("ViewModel", "View", StringComparison.Ordinal);
        L($"viewName: {viewName}");

        var viewType = currentPageType.Assembly.GetType(viewName!);
        L($"Assembly.GetType result: {(viewType == null ? "null" : viewType.FullName)}");

        var pageContent = window.GetActivePageContent();
        L($"GetActivePageContent result: {(pageContent == null ? "null" : pageContent.GetType().FullName)}");

        if (pageContent != null)
        {
            // Test FindLogicalControl directly
            var foundButton = ((ILogical)pageContent).FindLogicalControl<Avalonia.Controls.Button>("LoadButton");
            L($"FindLogicalControl<Button>(LoadButton): {(foundButton == null ? "null" : "found")}");

            if (foundButton != null)
            {
                L($"  Button.Command: {(foundButton.Command == null ? "null" : foundButton.Command.GetType().Name)}");
                var canExec = foundButton.Command?.CanExecute(foundButton.CommandParameter);
                L($"  CanExecute: {canExec}");
            }

            // List ALL controls with names (including empty name check)
            foreach (var node in ((ILogical)pageContent).GetSelfAndLogicalDescendants())
                if (node is Avalonia.Controls.Control c)
                {
                    var nameVal = c.Name ?? "(null)";
                    L($"  Control: {c.GetType().Name} Name=\"{nameVal}\"");
                }

            // Try ClickButton
            var clicked = pageContent.ClickButton("LoadButton");
            L($"ClickButton result: {clicked}");
        }

        window.Close();

        // Output all findings as assertions so they appear in test output
        foreach (var line in _log)
            Assert.True(true, line);
    }
}
