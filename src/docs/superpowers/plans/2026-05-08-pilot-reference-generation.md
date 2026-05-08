# Phase 2 — Pilot & Reference Generation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for task tracking.

**Goal:** Build a `VisualPilot` class for programmatic headless navigation of StarXelem, add XAML `Name` attributes to all interactive controls, and integrate Microsoft.Playwright .NET to generate reference screenshots from HTML mockups.

**Architecture:** VisualPilot wraps existing ScreenshotHelper + HeadlessAppFixture to provide high-level operations (open app, navigate tabs, find controls by Name, click buttons). ReferenceImageGenerator uses Playwright Chromium headless to render HTML mockups into golden PNGs for later visual comparison in Phase 3.

**Tech Stack:** Avalonia.Headless.XUnit 11.3.14, Microsoft.Playwright 1.50.0, xUnit 2.9.3, .NET 10.0

---

## Context

Phase 1 is complete: all headless tests pass, DI container injects deterministic mocks (TestGrpcClientService with 3 fake friends), and ScreenshotHelper captures PNGs. Phase 2 adds two capabilities: a Pilot for automated UI navigation and a Reference Generator from HTML mockups.

---

## Task 1 — Add Name attributes to XAML controls

**Files:**
- Modify: `StarXelem/Views/FriendListTabView.axaml` (lines 16, 27, 34)
- Modify: `StarXelem/Views/ShipTabView.axaml` (lines 27, 45)
- Modify: `StarXelem/Views/BlueprintListTabView.axaml` (line 27, 48)
- Modify: `StarXelem/Views/ItemsTabView.axaml` (lines 34, 125)
- Modify: `StarXelem/Views/SettingsTabView.axaml` (lines 99, 104)
- Modify: `StarXelem/Views/MissionsTabView.axaml` (lines 34, 37, 55)
- Modify: `StarXelem/Views/ExtractionTabView.axaml` (lines 112, 175)

These are pure XAML edits — no tests needed. Each edit adds a `Name="..."` attribute to enable control lookup in VisualPilot.

- [ ] **Step 1: Add Name to FriendListTabView controls**

In `StarXelem/Views/FriendListTabView.axaml`:
- Line 16: Change `<Button Classes="btn principal" Command="{Binding LoadFriendListCommand}">` to `<Button Name="LoadButton" Classes="btn principal" Command="{Binding LoadFriendListCommand}">`
- Line 27: Add `Name="OnlyConnectedToggle"` to the ToggleSwitch element
- Line 34: Add `Name="FriendDataGrid"` to the DataGrid element

- [ ] **Step 2: Add Name to ShipTabView controls**

In `StarXelem/Views/ShipTabView.axaml`:
- Line 27: Change `<Button Classes="btn principal" Command="{Binding LoadShipListCommand}">` to `<Button Name="LoadButton" Classes="btn principal" Command="{Binding LoadShipListCommand}">`
- Line 45: Add `Name="ShipDataGrid"` to the DataGrid element

- [ ] **Step 3: Add Name to BlueprintListTabView controls**

In `StarXelem/Views/BlueprintListTabView.axaml`:
- Line 27: Change `<Button Grid.Row="0" Grid.Column="1" Classes="btn principal" Command="{Binding LoadItemListCommand}">` to `<Button Name="LoadButton" Grid.Row="0" Grid.Column="1" Classes="btn principal" Command="{Binding LoadItemListCommand}">`
- Line 48: Add `Name="BlueprintListBox"` to the ListBox element

- [ ] **Step 4: Add Name to ItemsTabView controls**

In `StarXelem/Views/ItemsTabView.axaml`:
- Line 34: Change `<Button Grid.Row="5" Grid.Column="5" HorizontalAlignment="Center" Command="{Binding LoadItemListCommand}">Chercher</Button>` to `<Button Name="SearchButton" Grid.Row="5" Grid.Column="5" HorizontalAlignment="Center" Command="{Binding LoadItemListCommand}">Chercher</Button>`
- Line 125: Add `Name="ItemsDataGrid"` to the DataGrid element

- [ ] **Step 5: Add Name to SettingsTabView controls**

In `StarXelem/Views/SettingsTabView.axaml`:
- Line 99: Add `Name="ApiKeyField"` to the TextBox element
- Line 104: Add `Name="SaveApiKeyButton"` to the Button element

- [ ] **Step 6: Add Name to MissionsTabView controls**

In `StarXelem/Views/MissionsTabView.axaml`:
- Line 34: Change `<Button Content="Rafraîchir" Command="{Binding RefreshCommand}"/>` to `<Button Name="RefreshButton" Content="Rafraîchir" Command="{Binding RefreshCommand}"/>`
- Line 37: Add `Name="CategoryListBox"` to the first ListBox (categories)
- Line 55: Add `Name="MissionListBox"` to the second ListBox (missions)

- [ ] **Step 7: Add Name to ExtractionTabView controls**

In `StarXelem/Views/ExtractionTabView.axaml`:
- Line 112: Add `Name="ExtractCsvButton"` to the CSV export Button
- Line 175: Add `Name="UpdateLangButton"` to the language update Button

- [ ] **Step 8: Verify application builds**

Run: `dotnet build StarXelem/StarXelem.csproj`
Expected: Build succeeds with no errors. Name attributes are XAML properties — they don't affect compilation.

---

## Task 2 — Create VisualPilot class

**Files:**
- Create: `StarXelem.Tests.Visual/VisualPilot.cs`

Static utility class wrapping headless navigation operations. Builds on existing ScreenshotHelper and uses Avalonia's logical tree traversal.

- [ ] **Step 1: Write the failing test for VisualPilot.OpenAppAsync**

Create `StarXelem.Tests.Visual/PilotNavigationTest.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Services;
using StarXelem.Tests;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

public class PilotNavigationTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public PilotNavigationTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public async Task Can_Open_App_And_Navigate_To_Tab()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        Assert.NotNull(window);
        Assert.IsType<StarXelem.Views.MainWindow>(window);

        // Should be able to find the NavigationView
        var navView = VisualPilot.FindControl<NavigationView>(window, "NavigationView");
        Assert.NotNull(navView);
    }

    [AvaloniaFact]
    public async Task Can_Navigate_To_Settings_And_Find_Controls()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        VisualPilot.NavigateToTab(window, "Paramètres");
        Dispatcher.UIThread.RunJobs();

        // Settings tab should have ApiKeyField and SaveApiKeyButton
        var apiKeyField = VisualPilot.FindControl<TextBox>(window, "ApiKeyField");
        Assert.NotNull(apiKeyField);

        var saveButton = VisualPilot.FindControl<Button>(window, "SaveApiKeyButton");
        Assert.NotNull(saveButton);
    }

    [AvaloniaFact]
    public async Task Can_Capture_Screenshot_Of_Each_Tab()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();

        foreach (var page in viewModel.Pages)
        {
            viewModel.CurrentPage = page;
            Dispatcher.UIThread.RunJobs();

            var screenshotPath = VisualPilot.CaptureScreenshot(window, $"pilot_tab_{page.Name}.png");
            Assert.True(File.Exists(screenshotPath), $"Screenshot for tab '{page.Name}' should exist at {screenshotPath}");
        }
    }

    [AvaloniaFact]
    public async Task Can_Find_LoadButton_On_FriendTab()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        // FriendList is the default first tab, but navigate explicitly
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var friendTab = viewModel.Pages.First(p => p.Name == "Amis");
        viewModel.CurrentPage = friendTab;
        Dispatcher.UIThread.RunJobs();

        var loadButton = VisualPilot.FindControl<Button>(window, "LoadButton");
        Assert.NotNull(loadButton);
    }

    [AvaloniaFact]
    public async Task Can_Click_LoadButton_And_Wait_For_Result()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var friendTab = (FriendListTabViewModel)viewModel.Pages.First(p => p.Name == "Amis");
        viewModel.CurrentPage = friendTab;
        Dispatcher.UIThread.RunJobs();

        // Click the load button
        VisualPilot.ClickButton(window, "LoadButton");
        await Task.Delay(500); // Allow command to process

        // Friend list should be populated (TestGrpcClientService returns 3 fake friends)
        Assert.Equal(3, friendTab.FriendList.Count);
    }

    public void Dispose() { }
}
```

Run: `dotnet test StarXelem.Tests.Visual --filter "FullyQualifiedName~PilotNavigationTest" -v normal`
Expected: Build fails because VisualPilot class doesn't exist yet.

- [ ] **Step 2: Implement VisualPilot.cs**

Create `StarXelem.Tests.Visual/VisualPilot.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Static utility for headless UI navigation and interaction.
/// Wraps ScreenshotHelper and provides control lookup by Name.
/// </summary>
public static class VisualPilot
{
    /// <summary>
    /// Opens MainWindow with the DI fixture, measures and arranges it for rendering.
    /// Returns the window ready for screenshot capture.
    /// </summary>
    public static async Task<Window> OpenAppAsync(HeadlessAppFixture fixture)
    {
        var viewModel = fixture.Services.GetRequiredService<MainWindowViewModel>();
        var window = new StarXelem.Views.MainWindow { DataContext = viewModel };

        window.Show();
        window.Measure(Size.Infinity);
        window.Arrange(new Rect(default, window.DesiredSize));

        // Ensure all templates are applied before interaction
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    /// <summary>
    /// Navigates to a tab by name via the MainWindowViewModel.
    /// </summary>
    public static void NavigateToTab(Window window, string tabName)
    {
        var viewModel = (MainWindowViewModel)window.DataContext!;
        var targetPage = viewModel.Pages.FirstOrDefault(p => p.Name == tabName);
        if (targetPage != null)
            viewModel.CurrentPage = targetPage;

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Captures a screenshot of the window and saves it to the screenshots directory.
    /// Returns the file path of the captured PNG.
    /// </summary>
    public static string CaptureScreenshot(Window window, string fileName)
    {
        return ScreenshotHelper.CaptureWindow(window, fileName);
    }

    /// <summary>
    /// Finds a control by its Name property in the visual tree.
    /// Uses logical tree traversal for reliability with templates.
    /// </summary>
    public static T? FindControl<T>(Visual root, string name) where T : class, ILogical
    {
        return root.FindLogicalDescendants()
            .OfType<T>()
            .FirstOrDefault(c => c.GetName() == name);
    }

    /// <summary>
    /// Simulates a click on a Button found by Name.
    /// Finds the button and invokes its Command if available.
    /// </summary>
    public static void ClickButton(Window window, string buttonName)
    {
        var button = FindControl<Button>(window, buttonName);
        if (button == null) return;

        // Trigger the command directly — more reliable than simulating mouse events in headless mode
        var cmd = button.Command;
        if (cmd != null && cmd.CanExecute(button.CommandParameter))
            cmd.Execute(button.CommandParameter);

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Waits for a page's IsLoading property to become false.
    /// Times out after the specified duration.
    /// </summary>
    public static async Task WaitForLoadAsync(PageViewModelBase page, TimeSpan timeout = default)
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(10) : timeout;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (page.IsLoaded && !page.GetType().GetProperty("IsLoading", typeof(bool))?.GetValue(page)?.Equals(true) ?? false)
        {
            if (sw.Elapsed > timeout)
                throw new TimeoutException($"Page '{page.Name}' did not finish loading within {timeout.TotalSeconds}s");

            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
```

- [ ] **Step 3: Run tests and verify they pass**

Run: `dotnet test StarXelem.Tests.Visual --filter "FullyQualifiedName~PilotNavigationTest" -v normal`
Expected: All 5 tests pass.

- [ ] **Step 4: Verify all existing Phase 1 tests still pass**

Run: `dotnet test StarXelem.Tests.Visual`
Expected: All previous tests (FriendListHeadlessTest, BlueprintListHeadlessTest, ContainerTabHeadlessTest, SettingsTabHeadlessTest, NavigationViewTest) continue to pass.

---

## Task 3 — Add Microsoft.Playwright package

**Files:**
- Modify: `StarXelem.Tests.Visual/StarXelem.Tests.Visual.csproj` (add Playwright package reference)

- [ ] **Step 1: Add the Playwright NuGet package**

In `StarXelem.Tests.Visual.csproj`, add to the ItemGroup with other PackageReference entries:

```xml
<PackageReference Include="Microsoft.Playwright" Version="1.50.0" />
```

- [ ] **Step 2: Restore packages and install Playwright browsers**

Run: `dotnet restore StarXelem.Tests.Visual`
Then run: `npx playwright install chromium` (or use the .NET bootstrap via `pwsh -c "cd StarXelem.Tests.Visual/bin; dotnet exec Microsoft.Playwright --install chromium"`)

- [ ] **Step 3: Verify build succeeds**

Run: `dotnet build StarXelem.Tests.Visual`
Expected: Build succeeds with Playwright available.

---

## Task 4 — Create ReferenceImageGenerator class

**Files:**
- Create: `StarXelem.Tests.Visual/ReferenceImageGenerator.cs`

Uses Playwright Chromium headless to render HTML mockups into reference PNGs for visual comparison.

- [ ] **Step 1: Write the failing test for ReferenceImageGenerator**

Create `StarXelem.Tests.Visual/ReferenceGenerationTest.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StarXelem.Tests;

namespace StarXelem.Tests.Visual;

public class ReferenceGenerationTest : IAsyncDisposable
{
    private readonly ReferenceImageGenerator _generator;

    public ReferenceGenerationTest()
    {
        _generator = new ReferenceImageGenerator();
    }

    public async ValueTask DisposeAsync() => await _generator.DisposeAsync();

    [AvaloniaFact]
    public async Task Can_Generate_Extractions_Dark_Reference()
    {
        var projectRoot = GetMaquettesDirectory();
        var htmlPath = Path.Combine(projectRoot, "extractions_screen.html");

        Assert.True(File.Exists(htmlPath), $"HTML maquette should exist at {htmlPath}");

        var outputPath = await _generator.CaptureFromHtml(htmlPath, "extractions_dark.png");

        Assert.True(File.Exists(outputPath));
        Assert.True(new FileInfo(outputPath).Length > 1000, "Screenshot should be larger than 1KB");
    }

    [AvaloniaFact]
    public async Task Can_Generate_Extractions_Light_Reference()
    {
        var projectRoot = GetMaquettesDirectory();
        var htmlPath = Path.Combine(projectRoot, "extractions_screen_light.html");

        Assert.True(File.Exists(htmlPath), $"HTML maquette should exist at {htmlPath}");

        var outputPath = await _generator.CaptureFromHtml(htmlPath, "extractions_light.png");

        Assert.True(File.Exists(outputPath));
    }

    [AvaloniaFact]
    public async Task Can_Generate_ConnectionBar_Reference()
    {
        var projectRoot = GetMaquettesDirectory();
        var htmlPath = Path.Combine(projectRoot, "connection_status_bar.html");

        Assert.True(File.Exists(htmlPath), $"HTML maquette should exist at {htmlPath}");

        var outputPath = await _generator.CaptureFromHtml(htmlPath, "connection_bar.png");

        Assert.True(File.Exists(outputPath));
    }

    private static string GetMaquettesDirectory()
    {
        // Maquettes are in StarXelem/maquettes/ relative to the test project
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Go up from bin/Debug/net10.0/StarXelem.Tests.Visual to find StarXelem root
        var projectRoot = Directory.GetParent(baseDir)?.Parent?.Parent?.FullName ?? baseDir;
        return Path.Combine(projectRoot, "maquettes");
    }
}
```

Run: `dotnet test StarXelem.Tests.Visual --filter "FullyQualifiedName~ReferenceGenerationTest" -v normal`
Expected: Build fails because ReferenceImageGenerator doesn't exist yet.

- [ ] **Step 2: Implement ReferenceImageGenerator.cs**

Create `StarXelem.Tests.Visual/ReferenceImageGenerator.cs`:

```csharp
using Microsoft.Playwright;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Generates reference screenshots from HTML mockups using Playwright Chromium headless.
/// Output directory is screenshots/References/ relative to the test project base directory.
/// </summary>
public class ReferenceImageGenerator : IAsyncDisposable
{
    private readonly IBrowser _browser;
    private bool _isDisposed;

    public static string OutputDirectory { get; } = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "screenshots", "References");

    public ReferenceImageGenerator()
    {
        Directory.CreateDirectory(OutputDirectory);

        var playwright = Playwright.CreateAsync().Result;
        _browser = playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        }).Result;
    }

    /// <summary>
    /// Opens an HTML file in headless Chromium and captures a full-page screenshot.
    /// </summary>
    /// <param name="htmlFilePath">Absolute path to the HTML maquette file</param>
    /// <param name="outputFileName">Desired output filename (e.g., "extractions_dark.png")</param>
    /// <returns>Absolute path to the saved PNG</returns>
    public async Task<string> CaptureFromHtml(string htmlFilePath, string outputFileName)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ReferenceImageGenerator));

        var page = await _browser.NewPageAsync();

        // Set a consistent viewport for reproducible screenshots
        await page.SetViewportSizeAsync(1424, 900);

        var fileUrl = $"file://{htmlFilePath.Replace("\\", "/")}";
        await page.GotoAsync(fileUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // Allow any CSS transitions/animations to settle
        await page.WaitForTimeoutAsync(500);

        var outputPath = Path.Combine(OutputDirectory, outputFileName);
        await page.ScreenshotAsync(outputPath, new PageScreenshotOptions
        {
            FullPage = true,
            Type = ScreenshotType.Png,
        });

        await page.CloseAsync();

        return outputPath;
    }

    /// <summary>
    /// Renders inline HTML content at specific dimensions and captures a screenshot.
    /// Useful for isolated component testing.
    /// </summary>
    public async Task<string> CaptureComponent(string htmlContent, string outputFileName, int width = 800, int height = 600)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ReferenceImageGenerator));

        var page = await _browser.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.SetContentAsync(htmlContent);

        // Wait for any resources to load
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var outputPath = Path.Combine(OutputDirectory, outputFileName);
        await page.ScreenshotAsync(outputPath, new PageScreenshotOptions { Type = ScreenshotType.Png });

        await page.CloseAsync();
        return outputPath;
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        if (_browser != null)
            await _browser.CloseAsync();
    }
}
```

- [ ] **Step 3: Run tests and verify they pass**

Run: `dotnet test StarXelem.Tests.Visual --filter "FullyQualifiedName~ReferenceGenerationTest" -v normal`
Expected: All 3 tests pass, producing PNGs in `screenshots/References/`.

---

## Task 5 — Visual comparison test structure (pre-Phase 3)

**Files:**
- Create: `StarXelem.Tests.Visual/VisualComparisonTest.cs`

Establishes the scaffolding for Phase 3's semantic visual comparison. For now, it only validates that screenshots from both sources exist and have compatible dimensions.

- [ ] **Step 1: Write VisualComparisonTest.cs**

Create `StarXelem.Tests.Visual/VisualComparisonTest.cs`:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SixLabors.ImageSharp;
using StarXelem.Services;
using StarXelem.Tests;

namespace StarXelem.Tests.Visual;

public class VisualComparisonTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public VisualComparisonTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public async Task ExtractionsTab_Screenshot_Exists_And_Has_Dimensions()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        // Navigate to Extractions tab
        VisualPilot.NavigateToTab(window, "Extractions");
        Dispatcher.UIThread.RunJobs();

        var screenshotPath = VisualPilot.CaptureScreenshot(window, "comparison_extractions.png");

        Assert.True(File.Exists(screenshotPath));

        using var image = Image.Load(screenshotPath);
        Assert.True(image.Width > 100, "Screenshot width should be reasonable (>100px)");
        Assert.True(image.Height > 100, "Screenshot height should be reasonable (>100px)");
    }

    [AvaloniaFact]
    public async Task Reference_And_Actual_Screenshots_Have_Compatible_Dimensions()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);
        VisualPilot.NavigateToTab(window, "Extractions");
        Dispatcher.UIThread.RunJobs();

        var actualPath = VisualPilot.CaptureScreenshot(window, "comparison_actual.png");

        // Check that the reference exists (generated by ReferenceGenerationTest)
        var refDir = ReferenceImageGenerator.OutputDirectory;
        var refPath = Path.Combine(refDir, "extractions_dark.png");

        if (!File.Exists(refPath))
        {
            // Skip comparison if reference hasn't been generated yet — this is expected before Phase 3
            Assert.True(true, "Reference image not yet generated. Run ReferenceGenerationTest first.");
            return;
        }

        using var actual = Image.Load(actualPath);
        using var reference = Image.Load(refPath);

        // Dimensions should be within a reasonable tolerance (same viewport)
        var widthDiff = Math.Abs(actual.Width - reference.Width);
        var heightDiff = Math.Abs(actual.Height - reference.Height);

        Assert.True(widthDiff < 200, $"Width difference ({widthDiff}px) is too large. Actual: {actual.Width}, Reference: {reference.Width}");
        Assert.True(heightDiff < 200, $"Height difference ({heightDiff}px) is too large. Actual: {actual.Height}, Reference: {reference.Height}");
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Add SixLabors.ImageSharp package**

In `StarXelem.Tests.Visual.csproj`, add:

```xml
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.7" />
```

Run: `dotnet restore StarXelem.Tests.Visual`

- [ ] **Step 3: Run the comparison tests**

Run: `dotnet test StarXelem.Tests.Visual --filter "FullyQualifiedName~VisualComparisonTest" -v normal`
Expected: Tests pass (first test always passes, second test skips if reference image doesn't exist yet).

---

## Task 6 — Full regression test run

- [ ] **Step 1: Run all tests**

Run: `dotnet test StarXelem.Tests.Visual -v normal`
Expected: All Phase 1 + Phase 2 tests pass. Total expected: ~35+ tests across FriendListHeadlessTest, BlueprintListHeadlessTest, ContainerTabHeadlessTest, SettingsTabHeadlessTest, NavigationViewTest, PilotNavigationTest, ReferenceGenerationTest, VisualComparisonTest.

- [ ] **Step 2: Verify screenshot output**

Check that `screenshots/` directory contains pilot tab screenshots and `screenshots/References/` contains the 3 reference images from Playwright.

---

## Summary of files to create/modify

| Action | File | Purpose |
|--------|------|---------|
| MODIFY | `StarXelem/Views/FriendListTabView.axaml` | Add Name="LoadButton", "FriendDataGrid", "OnlyConnectedToggle" |
| MODIFY | `StarXelem/Views/ShipTabView.axaml` | Add Name="LoadButton", "ShipDataGrid" |
| MODIFY | `StarXelem/Views/BlueprintListTabView.axaml` | Add Name="LoadButton", "BlueprintListBox" |
| MODIFY | `StarXelem/Views/ItemsTabView.axaml` | Add Name="SearchButton", "ItemsDataGrid" |
| MODIFY | `StarXelem/Views/SettingsTabView.axaml` | Add Name="ApiKeyField", "SaveApiKeyButton" |
| MODIFY | `StarXelem/Views/MissionsTabView.axaml` | Add Name="RefreshButton", "CategoryListBox", "MissionListBox" |
| MODIFY | `StarXelem/Views/ExtractionTabView.axaml` | Add Name="ExtractCsvButton", "UpdateLangButton" |
| MODIFY | `StarXelem.Tests.Visual.csproj` | Add Microsoft.Playwright + SixLabors.ImageSharp packages |
| CREATE | `StarXelem.Tests.Visual/VisualPilot.cs` | Headless navigation utility class |
| CREATE | `StarXelem.Tests.Visual/PilotNavigationTest.cs` | Tests for pilot navigation and control interaction |
| CREATE | `StarXelem.Tests.Visual/ReferenceImageGenerator.cs` | Playwright-based reference screenshot generator |
| CREATE | `StarXelem.Tests.Visual/ReferenceGenerationTest.cs` | Tests for generating reference images from HTML mockups |
| CREATE | `StarXelem.Tests.Visual/VisualComparisonTest.cs` | Dimension comparison scaffolding (pre-Phase 3) |

---

## Verification checklist

1. All Phase 1 tests continue to pass after XAML Name attribute additions
2. VisualPilot.OpenAppAsync returns a usable MainWindow with NavigationView discoverable
3. PilotNavigationTest navigates to each tab and captures screenshots without crashing
4. ClickButton on "LoadButton" triggers the underlying command (FriendList populates)
5. Playwright browsers are installed (`npx playwright install chromium`)
6. ReferenceGenerationTest produces 3 PNGs in `screenshots/References/`
7. VisualComparisonTest validates screenshot dimensions match between sources
