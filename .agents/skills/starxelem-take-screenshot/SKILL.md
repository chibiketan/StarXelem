---
name: starxelem-take-screenshot
description: Launch the StarXelem Avalonia app to produce a screenshot of a specific tab for visual verification of UI changes. Use this skill after modifying any view (AXAML) or ViewModel affecting the UI, so you can read the resulting image and verify the changes look correct.
---

# StarXelem — Take Screenshot

Launch the StarXelem desktop app headlessly, navigate to a specific tab,
capture the full window as an image, then close automatically. You can then
read the image file to verify UI changes.

## Prerequisites

1. **Build the project first** (or ensure it is up to date):
   ```
   dotnet build src\StarXelem\StarXelem.csproj
   ```

2. **A P4K file must be available.** The app needs a Star Citizen `Data.p4k`
   to load. The app auto-detects installed paths from the Windows Registry,
   or you can have one saved in the settings. If no P4K is found, the
   screenshot will show the P4K selection screen.

## Command

```powershell
& "src\StarXelem\bin\Debug\net10.0\StarXelem.exe" `
  --screen <tab> `
  --screenshot <output_path.jpg> `
  --close
```

### Arguments

| Flag | Required | Description |
|---|---|---|
| `--screen <tab>` | optional | Tab to navigate to before capture. See [Tab Names](#tab-names) below. Default: first tab (`ship`). |
| `--screenshot <path>` | required | Output file path. Supports `.jpg` and `.png`. Relative paths are resolved from the working directory. |
| `--close` | optional | Close the app after the screenshot is taken. Without this flag the app stays open. |

### Tab Names

| Value | Tab (French) | Description |
|---|---|---|
| `ship` | Mon hangar | Ships from gRPC (game connection) |
| `p4kship` | Loadout vaisseaux | Ships from P4K (full catalogue) |
| `items` | Objets | Items catalogue |
| `blueprints` | Blueprints | Crafting blueprints |
| `friends` | Amis | Friends list |
| `missions` | Missions | Mission contracts |
| `extractions` | Extractions | Extraction data |
| `reputations` | Reputations | Faction reputations |
| `settings` | Paramètres | App settings |

## How It Works

1. The app starts and shows the MainWindow
2. If `--screen` is set, it navigates to the matching tab after a 200ms delay
3. If `--screenshot` is set, the app waits for the P4K to reach `CacheLoaded`
   state (timeout 5s) and the database to be ready (timeout 5s)
4. A 500ms render delay ensures the UI is fully painted
5. The full window is captured via `RenderTargetBitmap` and saved to disk
6. If `--close` is set, the app shuts down after a 500ms post-capture delay

**Total time:** ~3-10 seconds depending on P4K load speed.

## Step-by-Step Workflow

### 1. Build the project

```powershell
dotnet build src\StarXelem\StarXelem.csproj
```

Verify: `StarXelem -> ...\bin\Debug\net10.0\StarXelem.dll` with 0 errors.

### 2. Run with screenshot arguments

```powershell
& "src\StarXelem\bin\Debug\net10.0\StarXelem.exe" `
  --screen p4kship `
  --screenshot "C:\Users\Grego\AppData\Local\Temp\opencode\screenshot_p4kship.jpg" `
  --close
```

### 3. Read the screenshot

Use the Read tool to open the image file and visually inspect the result:

```
Read("C:\Users\Grego\AppData\Local\Temp\opencode\screenshot_p4kship.jpg")
```

### 4. Compare with expected UI

Check that your changes appear correctly:
- Column headers, labels, icons
- Data values displayed
- Layout, alignment, colors
- Error states or empty states if applicable

## Practical Examples

### Screenshot the P4K ships tab (most common)

```powershell
& "src\StarXelem\bin\Debug\net10.0\StarXelem.exe" `
  --screen p4kship `
  --screenshot "C:\Users\Grego\AppData\Local\Temp\opencode\p4kship.jpg" `
  --close
```

### Screenshot the items tab

```powershell
& "src\StarXelem\bin\Debug\net10.0\StarXelem.exe" `
  --screen items `
  --screenshot "C:\Users\Grego\AppData\Local\Temp\opencode\items.jpg" `
  --close
```

### Screenshot the default tab (Mon hangar)

```powershell
& "src\StarXelem\bin\Debug\net10.0\StarXelem.exe" `
  --screenshot "C:\Users\Grego\AppData\Local\Temp\opencode\default.jpg" `
  --close
```

### Screenshot in PNG (lossless, larger file)

```powershell
& "src\StarXelem\bin\Debug\net10.0\StarXelem.exe" `
  --screen blueprints `
  --screenshot "C:\Users\Grego\AppData\Local\Temp\opencode\blueprints.png" `
  --close
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Screenshot shows empty/blank window | P4K not found or load timeout | Ensure a Data.p4k is installed and detectable from the Registry |
| Screenshot shows P4K selection screen | No P4K auto-selected | The app needs a P4K path in its settings. Run the app manually once to select one. |
| App stays open after capture | `--close` not passed or crashed | Add `--close` flag. Check console for errors. |
| File not found error on launch | Wrong build config | Use `bin\Debug\net10.0\` or `bin\Release\net10.0\` matching your build |
| Capture is cropped or wrong size | Window not fully rendered | The built-in 500ms delay handles this. Increase if needed by editing `MainWindowViewModel.cs`. |
| Console shows "Onglet CLI inconnu" | Invalid tab name | Check spelling against the [Tab Names](#tab-names) table. Names are case-insensitive. |

## Important Notes

- **Output directory:** Use a temporary directory as `%LOCALAPPDATA%\Temp\opencode\` as the
  default output path — it is pre-approved for external directory access.
- **Timing:** The app waits up to 5s for P4K + 5s for DB before capturing.
  If the P4K is large or the disk is slow, the screenshot may show a loading
  state. This is normal on first run (cold cache).
- **Window size:** The capture uses the current window bounds. If the window
  was resized on a previous run, the screenshot reflects that size.
- **gRPC tabs:** The `ship` (Mon hangar) tab requires the Star Citizen game
  to be running for data to appear. For offline verification, prefer `p4kship`
  which reads from the P4K file directly.
<!-- Generated by memories.sh at 2026-07-21T19:51:38.806Z -->