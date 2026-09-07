# Plan : Reconnaissance de signature radar à l'écran et overlay "N× Minéral"

## État d'exécution (2026-09-07)

Toutes les tâches 1 à 9 ont été implémentées et compilent (`dotnet build src\StarXelem.sln` → 0 erreur). Vérifications effectuées :

- **Task 2** : rebuild complet de la base sur un p4k réel (LIVE 4.10) → table `MineralSignatures` = **260 lignes** (26 minéraux × 10), valeurs identiques à l'annexe A / `MineralSignatures.md`, collisions confirmées (ex. `19200` → 5× Aslarite / 6× Savrilium). Deux entités parasites du p4k (`aphorite` signature 0, un nom localisé `"<= PLACEHOLDER =>"` non résolu) ont été découvertes lors de cette vérification et filtrées dans `MineralSignatureExtractor` (signature ≤ 0 exclue, nom contenant `"PLACEHOLDER"` exclu) — non prévu dans le plan initial, ajouté après coup.
- **Task 8** : capture d'écran réelle de l'onglet Paramètres → carte "Scan de signature" conforme. Un bug a été trouvé et corrigé : `HotkeyBox` (sous-classe de `TextBox`) n'héritait pas du template/style par défaut d'Avalonia (le control apparaissait invisible) — corrigé en surchargeant `StyleKeyOverride => typeof(TextBox)`.
- **Non vérifié manuellement** (nécessite le jeu Star Citizen lancé en fenêtré sans bordure + interaction clavier réelle) : capture GDI de la fenêtre du jeu (Task 3), qualité de détection OCR sur les 3 captures fournies par l'utilisateur (Task 4 — le pack de langue anglaise pour l'OCR Windows n'est pas installé sur la machine de dev, `IsOcrAvailable=false` a été confirmé par le bandeau d'avertissement affiché), apparence/comportement réel de l'overlay à l'écran (Task 5), déclenchement effectif du raccourci global (Task 6-7). **L'hypothèse §0.1 (signature = N × base) reste à valider en jeu**, cf. avertissement plus bas.
- Task 9 : `CLAUDE.md`, `MineableRocks.md` et `.gitignore` mis à jour ; mémoire projet à ajouter séparément.

---

> **Pour l'agent exécutant :** implémenter tâche par tâche, dans l'ordre. Chaque tâche se termine par un `dotnet build` vert (`src/StarXelem.sln`). Cocher les cases (`- [ ]`) au fur et à mesure. Les messages de commit sont en français au format conventional commits (`feat(scan): …`).
> Il n'y a pas de tests automatisés dans ce projet : chaque tâche contient une **procédure de vérification manuelle**.

**Objectif :** à la pression d'un raccourci clavier global (configurable, avec modificateurs), StarXelem capture l'écran du jeu Star Citizen, extrait par OCR la valeur de signature radar affichée sur le HUD (ex. `80,000`), la cherche dans une nouvelle table de la base locale (signature ↔ minéral × taille de cluster), et affiche pendant 5 s une petite fenêtre overlay sans bordure, non focusable, à 75 % d'opacité, à l'emplacement de la signature sur l'écran (ex. `5× Iron`). L'application n'a pas besoin d'avoir le focus.

**Architecture (vue d'ensemble) :**

```
[Hotkey global Win32]  ──►  ScanSignatureOrchestrator.RunAsync()
                                   │
        ┌──────────────────────────┼───────────────────────────────┐
        ▼                          ▼                               ▼
 IScreenCaptureService     ISignatureOcrService          IMineralSignatureRepository
 (GDI BitBlt fenêtre SC)   (Windows.Media.Ocr +          (EF Core, table MineralSignatures
                            pré-traitement SkiaSharp)     remplie au RebuildDb, phase 11)
        │                          │                               │
        └──────────► candidats {valeur, rect écran} ───────────────┘
                                   │
                                   ▼
                    IOverlayNotificationService
              (Window Avalonia sans bordure, topmost,
               WS_EX_NOACTIVATE|TOOLWINDOW|TRANSPARENT,
               Opacity 0.75, fermeture après 5 s)
```

**Stack :** C# / .NET 10, Avalonia 11.3, CommunityToolkit.Mvvm, EF Core SQLite 10, SkiaSharp (déjà présent transitivement via Avalonia), P/Invoke Win32 (`user32`, `gdi32`), OCR Windows intégré (`Windows.Media.Ocr`).

---

## 0. Décisions techniques et hypothèses à valider

### 0.1 Formule de signature d'un cluster (⚠️ hypothèse)

La signature radar d'un rocher est lue dans le p4k (`SSCSignatureSystemParams → radarProperties → baseSignatureParams → signatures[4]`, cf. `Documents/Datafiles/MineableRocks.md`). Le plan retient l'hypothèse demandée : **signature(cluster de N rochers) = N × signature(rocher)**, pour N de 1 à 10.

⚠️ **Sur les captures fournies, la valeur affichée est `80,000`, ce qui ne correspond à aucun multiple ≤ 10 des signatures connues** (la plus grande possible est 10 × Ice = 43 000). Causes possibles : rochers de plusieurs tailles dans un même cluster, facteur d'échelle appliqué par le jeu à l'affichage, cluster > 10 rochers, ou signatures du HUD arrondies. **Avant la tâche 2**, valider en jeu sur 2–3 clusters connus (scanner le cluster puis compter/scanner les rochers individuellement) que la formule N × base est correcte. Si elle ne l'est pas, seule la fonction de génération (`MineralSignatureGenerator`, tâche 2) est à adapter : le reste du plan reste valable.

Pour absorber une éventuelle légère différence, la recherche en base se fait avec une **tolérance configurable** (constante, défaut ± 0 puis à ajuster) et renvoie **toutes** les correspondances (les collisions existent : `19200` = 5× Aslarite ou 6× Savrilium, `28800`, `30600`, `38700`).

### 0.2 Capture d'écran

- Le jeu doit être en **fenêtré sans bordure** (borderless). En plein écran exclusif, ni la capture GDI ni l'overlay ne fonctionnent de manière fiable — à documenter dans l'UI des paramètres.
- Capture par **GDI `BitBlt` sur le DC écran**, limitée au rectangle de la fenêtre du jeu (`GetWindowRect` sur `Process.GetProcessesByName("StarCitizen").MainWindowHandle`). Pas de dépendance NuGet supplémentaire.
- Fallback si la fenêtre du jeu n'est pas trouvée : capture du moniteur où se trouve le curseur.

### 0.3 OCR

- **Choix retenu : `Windows.Media.Ocr`** (intégré à Windows 10/11, gratuit, aucun binaire à distribuer, rapide, très bon sur du texte HUD net). Nécessite :
  - passer le `TargetFramework` de `StarXelem.csproj` à `net10.0-windows10.0.19041.0` (les projections WinRT sont fournies par le SDK .NET, pas de package supplémentaire) ;
  - le pack de langue **anglais** installé sur la machine (vérifier `OcrEngine.AvailableRecognizerLanguages` au démarrage et afficher un message clair sinon).
- Pré-traitement de l'image avant OCR (SkiaSharp) pour fiabiliser la lecture : isolement des pixels **jaune/orange HUD** (teinte ~40–55°, saturation > 0.4, luminosité > 0.5), binarisation (texte noir sur fond blanc), agrandissement ×3.
  - **Post-mortem (2026-09-07)** : approche abandonnée. Un banc de test dédié (OCR Windows exécuté directement sur une vraie capture) a montré que binariser dégradait la lisibilité au lieu de l'améliorer — le texte HUD anti-aliasé, transmis tel quel en couleur (avec un agrandissement optionnel si besoin), est lu correctement par `Windows.Media.Ocr` sans aucun filtre de teinte/luminosité.
- Alternative si la qualité est insuffisante : NuGet `Tesseract` (charlesw) + `eng.traineddata` embarqué — à ne considérer qu'après échec constaté de l'OCR Windows.
- Le texte HUD à reconnaître a la forme `80,000` (séparateur de milliers). L'OCR renvoie des mots avec leurs boîtes englobantes ; on ne retient que les mots matchant `^\d{1,3}(,\d{3})+$|^\d{4,6}$` et dont la valeur est dans la plage plausible (≥ min(base) et ≤ 10 × max(base), soit env. 3 000 – 45 000 ; élargir si l'hypothèse 0.1 change).

### 0.4 Raccourci clavier global

- `RegisterHotKey` Win32 avec `hWnd = NULL` sur un **thread dédié** avec sa propre boucle `GetMessage` : le `WM_HOTKEY` arrive dans la file du thread, aucune fenêtre nécessaire, fonctionne quand le jeu a le focus. Le raccourci est **consommé** (le jeu ne le voit pas) : à mentionner dans l'UI.
- Ré-enregistrement à chaud quand le paramètre change (`PostThreadMessage(WM_QUIT)` puis redémarrage du thread).
- Si `RegisterHotKey` échoue (touche déjà prise par une autre appli), remonter l'erreur dans la page Paramètres.

### 0.5 Overlay

- `Window` Avalonia : `SystemDecorations="None"`, `TransparencyLevelHint="Transparent"`, `Background="Transparent"`, `ShowInTaskbar="False"`, `Topmost="True"`, `ShowActivated="False"`, `CanResize="False"`, `Focusable="False"`, `Opacity="0.75"` (= 25 % de transparence), `SizeToContent="WidthAndHeight"`.
- Après `Show()`, forcer via `SetWindowLongPtr(GWL_EXSTYLE)` les styles `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT` (ne vole jamais le focus, absent d'Alt-Tab/barre des tâches, clics traversants).
- `DispatcherTimer` 5 s → `Close()`. Une nouvelle détection pendant l'affichage remplace l'overlay courant (réinitialise le timer).
- Position : coin supérieur gauche = coin inférieur gauche de la boîte OCR de la signature, converti en coordonnées écran (`PixelPoint`), avec un décalage de +4 px.

### 0.6 Persistance des paramètres

Via `ISettingsService` existant (registre `HKCU\Software\StarXelem`), clés :

| Clé | Valeur | Défaut |
|---|---|---|
| `ScanHotkeyEnabled` | `"true"/"false"` | `"true"` |
| `ScanHotkeyKey` | nom de touche `Avalonia.Input.Key` (ex. `F9`) | `"F9"` |
| `ScanHotkeyModifiers` | flags `Avalonia.Input.KeyModifiers` sérialisés (ex. `"Control, Shift"`) | `"Control"` |

---

## Fichiers créés / modifiés

| Fichier | Action |
|---|---|
| `src/StarXelem/StarXelem.csproj` | TFM → `net10.0-windows10.0.19041.0`, `<SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>` |
| `src/StarXelem/Constants/DatabaseConstants.cs` | `DatabaseVersion` 1 → 2 |
| `src/StarXelem/Constants/ScanConstants.cs` | **Nouveau** : tailles de cluster, tolérance, regex, durée overlay, clés de settings |
| `src/StarXelem/Data/Entities.cs` | **Nouvelle** entité `MineralSignatureEntity` |
| `src/StarXelem/Data/StarXelemDbContext.cs` | `DbSet<MineralSignatureEntity>` + index |
| `src/StarXelem/Data/IMineralSignatureRepository.cs` / `MineralSignatureRepository.cs` | **Nouveaux** : recherche par signature |
| `src/StarXelem/Services/Mining/IMineralSignatureExtractor.cs` / `MineralSignatureExtractor.cs` | **Nouveaux** : extraction p4k minéral → signature de base (code déplacé depuis `ExtractionTabViewModel`) |
| `src/StarXelem/Services/Mining/MineralSignatureGenerator.cs` | **Nouveau** : génère les lignes 1..10 |
| `src/StarXelem/Services/LocalDatabaseService.cs` | Phase 11 `PopulateMineralSignaturesAsync` |
| `src/StarXelem/ViewModels/ExtractionTabViewModel.cs` | Utilise `IMineralSignatureExtractor` au lieu du code inline |
| `src/StarXelem/Services/Scan/Win32/NativeMethods.cs` | **Nouveau** : P/Invoke `user32`/`gdi32` |
| `src/StarXelem/Services/Scan/IScreenCaptureService.cs` / `GdiScreenCaptureService.cs` | **Nouveaux** |
| `src/StarXelem/Services/Scan/ISignatureOcrService.cs` / `WindowsSignatureOcrService.cs` | **Nouveaux** |
| `src/StarXelem/Services/Scan/IGlobalHotkeyService.cs` / `Win32GlobalHotkeyService.cs` | **Nouveaux** |
| `src/StarXelem/Services/Scan/IOverlayNotificationService.cs` / `OverlayNotificationService.cs` | **Nouveaux** |
| `src/StarXelem/Services/Scan/IScanSignatureOrchestrator.cs` / `ScanSignatureOrchestrator.cs` | **Nouveaux** : enchaîne capture → OCR → BDD → overlay |
| `src/StarXelem/Services/Scan/ScanHotkeySettings.cs` | **Nouveau** : record + (dé)sérialisation des settings |
| `src/StarXelem/Services/Scan/Design*.cs` | **Nouveaux** : implémentations inertes pour le mode design |
| `src/StarXelem/Models/ScanModels.cs` | **Nouveau** : `CapturedFrame`, `SignatureCandidate`, `SignatureMatch` |
| `src/StarXelem/Views/Overlay/SignatureOverlayWindow.axaml(.cs)` | **Nouveaux** |
| `src/StarXelem/ViewModels/Overlay/SignatureOverlayViewModel.cs` | **Nouveau** |
| `src/StarXelem/ViewModels/SettingsTabViewModel.cs` | Section raccourci de scan |
| `src/StarXelem/Views/SettingsTabView.axaml` | Carte "Scan de signature" |
| `src/StarXelem/Components/HotkeyBox.cs` | **Nouveau** : contrôle de saisie d'un raccourci |
| `src/StarXelem/Extensions/ServiceCollectionExtensions.cs` | Enregistrement DI |
| `src/StarXelem/App.axaml.cs` | Démarrage du hotkey après création de la `MainWindow` ; arrêt à la fermeture |
| `Documents/Datafiles/MineralSignatures.md` | **Nouveau** : table de référence 1..10 (générée, cf. annexe A) |

---

## Task 1 : Passage au TFM Windows et constantes

**Files :** `StarXelem.csproj`, `Constants/ScanConstants.cs`, `Constants/DatabaseConstants.cs`

- [ ] **Step 1** — Dans `StarXelem.csproj`, remplacer `<TargetFramework>net10.0</TargetFramework>` par `<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>` et ajouter `<SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>`. Vérifier que `Directory.Build.props` (qui fixe `net10.0`) est bien surchargé par le csproj (il l'est : le csproj est lu après). Les autres projets (`StarXelem.Cli`, `cli.testdb`) ne changent pas.
- [ ] **Step 2** — Créer `Constants/ScanConstants.cs` :

```csharp
namespace StarXelem.Constants;

public static class ScanConstants
{
    public const int MinClusterSize = 1;
    public const int MaxClusterSize = 10;
    /// <summary>Tolérance absolue admise entre la valeur OCR et la signature en base (à ajuster après validation en jeu).</summary>
    public const int SignatureTolerance = 0;
    public const int OverlayDurationMs = 5000;
    public const int OcrUpscaleFactor = 3;
    public const string SettingHotkeyEnabled = "ScanHotkeyEnabled";
    public const string SettingHotkeyKey = "ScanHotkeyKey";
    public const string SettingHotkeyModifiers = "ScanHotkeyModifiers";
    public const string DefaultHotkeyKey = "F9";
    public const string DefaultHotkeyModifiers = "Control";
    public const string StarCitizenProcessName = "StarCitizen";
}
```

- [ ] **Step 3** — `DatabaseConstants.DatabaseVersion = 2` (déclenche la reconstruction automatique de la base chez les utilisateurs existants via `NeedsRebuildCheckAsync`).
- [ ] **Step 4** — `dotnet build src/StarXelem.sln` : vert. Lancer l'application : comportement inchangé.

---

## Task 2 : Table `MineralSignatures` et génération 1..10

**Files :** `Data/Entities.cs`, `Data/StarXelemDbContext.cs`, `Data/IMineralSignatureRepository.cs`, `Data/MineralSignatureRepository.cs`, `Services/Mining/*`, `Services/LocalDatabaseService.cs`, `ViewModels/ExtractionTabViewModel.cs`

- [ ] **Step 1** — Ajouter à la fin de `Data/Entities.cs` :

```csharp
/// <summary>Signature radar attendue pour un cluster de <see cref="ClusterSize"/> rochers d'un même minéral.</summary>
public class MineralSignatureEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>Clé technique normalisée du minéral (minuscules, sans espaces), ex. "iron".</summary>
    [Required]
    public string MineralKey { get; set; } = string.Empty;

    /// <summary>Nom localisé affiché, ex. "Iron".</summary>
    [Required]
    public string MineralName { get; set; } = string.Empty;

    /// <summary>Rareté déduite du nom du record (Legendary/Epic/Rare/Uncommon/Common), informatif.</summary>
    public string Rarity { get; set; } = string.Empty;

    /// <summary>Signature d'un rocher isolé, lue dans le p4k.</summary>
    public int BaseSignature { get; set; }

    /// <summary>Nombre de rochers du cluster (1..10).</summary>
    public int ClusterSize { get; set; }

    /// <summary>Signature du cluster = BaseSignature × ClusterSize (cf. hypothèse 0.1).</summary>
    public int Signature { get; set; }
}
```

- [ ] **Step 2** — `StarXelemDbContext` : ajouter `public DbSet<MineralSignatureEntity> MineralSignatures => Set<MineralSignatureEntity>();` et dans `OnModelCreating` :

```csharp
modelBuilder.Entity<MineralSignatureEntity>().HasIndex(m => m.Signature);
modelBuilder.Entity<MineralSignatureEntity>().HasIndex(m => new { m.MineralKey, m.ClusterSize }).IsUnique();
```

- [ ] **Step 3** — Créer `Services/Mining/IMineralSignatureExtractor.cs` + `MineralSignatureExtractor.cs` en **déplaçant** la boucle "Build mineral-to-signature maps from mineable entities" de `ExtractionTabViewModel.UpdateLocalisationAsync` (`src/StarXelem/ViewModels/ExtractionTabViewModel.cs`, ~lignes 224–297). Conserver strictement les règles existantes (elles corrigent des bugs documentés dans `Documents/Datafiles/MineableRocks.md`) :
  - filtrer sur `RecordName.Contains(".MineableRock_", OrdinalIgnoreCase)` et exclure `"test"` ;
  - `EnsureRecordsDepthAsync([entity], 3)` avant lecture ;
  - signature = `signatures[4]` arrondie ; ignorer `3000` et `4000` ;
  - minéral = `compositionArray.FirstOrDefault()` uniquement ;
  - garder la première signature trouvée par nom localisé.

```csharp
public sealed record MineralBaseSignature(string MineralKey, string MineralName, string Rarity, int BaseSignature);

public interface IMineralSignatureExtractor
{
    /// <summary>Le p4k doit déjà être ouvert par l'appelant.</summary>
    Task<IReadOnlyList<MineralBaseSignature>> ExtractAsync(CancellationToken cancellationToken = default);
}
```

  `MineralKey` = même normalisation que `mineralKeyName` existant (mots en minuscules, suppression de `@ ( )`, mots exclus `deposit/ore/raw/items/commodities/r`). `Rarity` = segment entre `MineableRock_Asteroid`/`MineableRock_Surface` et `_` dans le `RecordName` (`Common`, `Uncommon`, `Rare`, `Epic`, `Legendary`), `""` sinon.

- [ ] **Step 4** — Refactorer `ExtractionTabViewModel.UpdateLocalisationAsync` pour appeler `_mineralSignatureExtractor.ExtractAsync()` et reconstruire localement `mineralSignatureMap` (nom localisé → signature) et `mineralSignatureMapLower` (clé → signature) à partir du résultat. Injecter `IMineralSignatureExtractor` dans le constructeur. Aucun changement fonctionnel attendu sur la génération du `global.ini`.

- [ ] **Step 5** — Créer `Services/Mining/MineralSignatureGenerator.cs` (statique, pur, sans dépendance) :

```csharp
public static class MineralSignatureGenerator
{
    public static IEnumerable<MineralSignatureEntity> Generate(IEnumerable<MineralBaseSignature> bases)
    {
        foreach (var b in bases)
            for (var n = ScanConstants.MinClusterSize; n <= ScanConstants.MaxClusterSize; n++)
                yield return new MineralSignatureEntity
                {
                    MineralKey = b.MineralKey, MineralName = b.MineralName, Rarity = b.Rarity,
                    BaseSignature = b.BaseSignature, ClusterSize = n, Signature = b.BaseSignature * n,
                };
    }
}
```

  C'est **le seul endroit** à modifier si l'hypothèse 0.1 est invalidée.

- [ ] **Step 6** — `LocalDatabaseService` : injecter `IMineralSignatureExtractor`, passer `TotalPhases` à 11, ajouter après la phase 10 :

```csharp
progress?.Report(new RebuildProgress(11, TotalPhases, "Chargement des signatures minérales…"));
phase.Restart();
await PopulateMineralSignaturesAsync(db, cancellationToken).ConfigureAwait(false);
phase.Stop();
_logger.LogInformation("[Phase 11/{Total}] Mineral signatures completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
```

  avec :

```csharp
private async Task PopulateMineralSignaturesAsync(StarXelemDbContext db, CancellationToken ct)
{
    var bases = await _mineralSignatureExtractor.ExtractAsync(ct).ConfigureAwait(false);
    db.MineralSignatures.AddRange(MineralSignatureGenerator.Generate(bases));
    await db.SaveChangesAsync(ct).ConfigureAwait(false);
    db.ChangeTracker.Clear();
    _logger.LogInformation("{Minerals} minéraux → {Rows} lignes de signatures.", bases.Count, bases.Count * ScanConstants.MaxClusterSize);
}
```

  ⚠️ Placer la phase **avant** `_p4kService.ReleaseHeavyCache()` (l'extracteur a besoin des `EntityClassDefinition`). Respecter le pattern `SaveChanges` + `ChangeTracker.Clear()` (cf. mémoire `localdb_rebuild_perf`).

- [ ] **Step 7** — Repository `Data/IMineralSignatureRepository.cs` / `MineralSignatureRepository.cs` (même pattern que `LocaleEntryRepository` : `IDbContextFactory`, `File.Exists(DbPath)`, `AsNoTracking`) :

```csharp
public interface IMineralSignatureRepository
{
    /// <summary>Toutes les lignes dont |Signature - value| ≤ tolerance, triées par écart puis ClusterSize.</summary>
    Task<IReadOnlyList<MineralSignatureEntity>> FindBySignatureAsync(int value, int tolerance, CancellationToken ct = default);
    Task<IReadOnlyList<MineralSignatureEntity>> GetAllAsync(CancellationToken ct = default);
}
```

- [ ] **Step 8** — DI (`ServiceCollectionExtensions`) : `IMineralSignatureExtractor` (singleton, pour les deux modes ; en design il retournera une liste vide car `DesignP4kService` n'a pas d'entités — vérifier qu'il ne lève pas), `IMineralSignatureRepository` (singleton). Ajouter le paramètre au lambda qui construit `LocalDatabaseService` (ligne ~59).
- [ ] **Step 9** — Générer `Documents/Datafiles/MineralSignatures.md` avec la table de l'annexe A (elle est déjà calculée ci-dessous ; la copier telle quelle et ajouter un paragraphe expliquant l'hypothèse 0.1 et les collisions).
- [ ] **Vérification** — Build vert. Lancer l'app : la base est reconstruite (version 2). Ouvrir `%LOCALAPPDATA%\StarXelem\database.db` (DB Browser for SQLite ou `sqlite3`) : `SELECT COUNT(*) FROM MineralSignatures;` → 26 minéraux × 10 = **260** lignes ; `SELECT * FROM MineralSignatures WHERE Signature = 21350;` → Iron, ClusterSize 5. Onglet Extraction → "Mettre à jour la localisation" fonctionne comme avant (les `(RS xxxx)` sont toujours présents dans le `global.ini` généré).

---

## Task 3 : Capture d'écran de la fenêtre du jeu

**Files :** `Services/Scan/Win32/NativeMethods.cs`, `Models/ScanModels.cs`, `Services/Scan/IScreenCaptureService.cs`, `Services/Scan/GdiScreenCaptureService.cs`

- [ ] **Step 1** — `Models/ScanModels.cs` :

```csharp
/// <summary>Image capturée + origine du rectangle capturé en coordonnées écran (pixels physiques).</summary>
public sealed record CapturedFrame(SKBitmap Bitmap, int ScreenX, int ScreenY) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>Nombre lu par l'OCR et sa boîte en coordonnées écran.</summary>
public sealed record SignatureCandidate(int Value, string RawText, PixelRect ScreenBounds);

public sealed record SignatureMatch(SignatureCandidate Candidate, IReadOnlyList<MineralSignatureEntity> Rows);
```

- [ ] **Step 2** — `NativeMethods.cs` (`internal static partial class`, `[LibraryImport]` + `SetLastError = true`) : `GetDC`, `ReleaseDC`, `CreateCompatibleDC`, `CreateCompatibleBitmap`, `SelectObject`, `BitBlt` (`SRCCOPY | CAPTUREBLT`), `DeleteObject`, `DeleteDC`, `GetDIBits`, `GetWindowRect`, `GetClientRect`, `ClientToScreen`, `IsIconic`, `RegisterHotKey`, `UnregisterHotKey`, `GetMessage`, `PostThreadMessage`, `SetWindowLongPtr`, `GetWindowLongPtr`, `MonitorFromPoint`, `GetMonitorInfo`, `GetCursorPos`, ainsi que les constantes (`WM_HOTKEY = 0x0312`, `WM_QUIT = 0x0012`, `MOD_ALT = 1`, `MOD_CONTROL = 2`, `MOD_SHIFT = 4`, `MOD_NOREPEAT = 0x4000`, `GWL_EXSTYLE = -20`, `WS_EX_TRANSPARENT = 0x20`, `WS_EX_TOOLWINDOW = 0x80`, `WS_EX_NOACTIVATE = 0x08000000`).

- [ ] **Step 3** — Interface + implémentation :

```csharp
public interface IScreenCaptureService
{
    /// <summary>Capture la zone client de la fenêtre Star Citizen ; à défaut, le moniteur sous le curseur. Null si rien n'est capturable.</summary>
    Task<CapturedFrame?> CaptureGameWindowAsync(CancellationToken ct = default);
}
```

  `GdiScreenCaptureService` : trouver le process (`Process.GetProcessesByName(ScanConstants.StarCitizenProcessName)` avec `MainWindowHandle != 0` et `!IsIconic`), calculer le rectangle client en coordonnées écran (`GetClientRect` + `ClientToScreen`), `BitBlt` depuis `GetDC(IntPtr.Zero)`, `GetDIBits` en BGRA 32 bits top-down (`biHeight` négatif) directement dans `SKBitmap` (`SKColorType.Bgra8888`, `SKAlphaType.Opaque`). Tout en `Task.Run` (les appels GDI sont synchrones, ~10–30 ms en 1440p). Logger la taille capturée et la durée.

- [ ] **Step 4** — Ajouter un mode debug : si la variable d'environnement `STARXELEM_SCAN_DEBUG_DIR` est définie, sauver `capture.png` dedans (`SKImage.Encode`). Utile pour toutes les vérifications suivantes.
- [ ] **Vérification** — Petit bouton temporaire (ou commande CLI `--scan-test`, à retirer en fin de plan) qui appelle le service : avec SC ouvert en borderless, `capture.png` contient bien l'image du jeu, sans la fenêtre StarXelem, aux bonnes dimensions (ex. 2560×1440). Sans SC : capture du moniteur.

---

## Task 4 : OCR et extraction de la signature

**Files :** `Services/Scan/ISignatureOcrService.cs`, `Services/Scan/WindowsSignatureOcrService.cs`

- [ ] **Step 1** — Interface :

```csharp
public interface ISignatureOcrService
{
    bool IsAvailable { get; }                    // moteur + langue 'en' disponibles
    string? UnavailableReason { get; }
    Task<IReadOnlyList<SignatureCandidate>> FindSignatureCandidatesAsync(CapturedFrame frame, CancellationToken ct = default);
}
```

- [ ] **Step 2** — Pré-traitement SkiaSharp (méthode interne `Preprocess(SKBitmap src) → SKBitmap`) :
  1. Parcours des pixels : conserver ceux dont la couleur est "HUD jaune/ambre" (convertir en HSL : teinte ∈ [35°, 60°], saturation ≥ 0,35, luminosité ≥ 0,45) → noir, le reste → blanc. Rendre les seuils des constantes privées pour pouvoir les ajuster.
  2. Agrandir ×`OcrUpscaleFactor` avec `SKFilterQuality.High` / `SKSamplingOptions` (le HUD est petit : `80,000` fait ~12 px de haut en 1440p).
  3. En mode debug, sauver `preprocessed.png`.
- [ ] **Step 3** — OCR : convertir le `SKBitmap` en `SoftwareBitmap` (`BitmapPixelFormat.Bgra8`, `BitmapAlphaMode.Premultiplied`) via `SoftwareBitmap.CreateCopyFromBuffer`, puis `OcrEngine.TryCreateFromLanguage(new Language("en"))` → `RecognizeAsync`. Pour chaque `OcrWord` : nettoyer (`Trim`, remplacer `O`→`0`, `l`/`I`→`1`, retirer les espaces), tester la regex `^\d{1,3}(,\d{3})+$|^\d{4,6}$`, parser en `int` (sans virgules), filtrer sur la plage plausible `[MinPlausibleSignature, MaxPlausibleSignature]` (constantes : 2 500 et 50 000). Convertir `word.BoundingRect` (coordonnées de l'image agrandie) en coordonnées écran : `/ OcrUpscaleFactor` puis `+ (frame.ScreenX, frame.ScreenY)`.
  - Attention : `Windows.Media.Ocr` limite la taille d'image à `OcrEngine.MaxImageDimension` (généralement 10 000 px) — si l'image agrandie dépasse, réduire le facteur.
  - Le HUD contient d'autres nombres (`0/19`, `6.5km`, `1990m`, `294`) : ils sont éliminés par la regex/la plage, et de toute façon par l'absence de correspondance en base.
- [ ] **Step 4** — Désambiguïsation si plusieurs candidats : conserver l'ordre "distance au centre de l'écran croissante" (le marqueur de scan est en général près du centre du HUD) ; l'orchestrateur affichera le premier qui matche en base.
- [ ] **Vérification** — Avec les 3 captures fournies par l'utilisateur (à placer dans `private/scan-samples/`, dossier ignoré par git), ajouter une commande CLI temporaire `--scan-image <png>` qui exécute pré-traitement + OCR et logge les candidats : `80000` doit être détecté sur les 3 images malgré les positions de tête différentes. Ajuster les seuils HSL si nécessaire en regardant `preprocessed.png`.

---

## Task 5 : Fenêtre overlay

**Files :** `Views/Overlay/SignatureOverlayWindow.axaml(.cs)`, `ViewModels/Overlay/SignatureOverlayViewModel.cs`, `Services/Scan/IOverlayNotificationService.cs`, `Services/Scan/OverlayNotificationService.cs`

- [ ] **Step 1** — `SignatureOverlayViewModel : ViewModelBase` avec `ObservableCollection<string> Lines` (ex. `"5× Iron"`, et en cas de collision plusieurs lignes `"5× Aslarite"`, `"6× Savrilium"`), `string Signature` (`"19 200"`).
- [ ] **Step 2** — `SignatureOverlayWindow.axaml` :

```xml
<Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:StarXelem.ViewModels.Overlay"
        x:Class="StarXelem.Views.Overlay.SignatureOverlayWindow" x:DataType="vm:SignatureOverlayViewModel"
        SystemDecorations="None" TransparencyLevelHint="Transparent" Background="Transparent"
        ShowInTaskbar="False" Topmost="True" ShowActivated="False" CanResize="False" Focusable="False"
        SizeToContent="WidthAndHeight" Opacity="0.75">
    <Border Background="#1E2328" BorderBrush="#E0B44C" BorderThickness="1" CornerRadius="6" Padding="10,6">
        <ItemsControl ItemsSource="{Binding Lines}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="x:String">
                    <TextBlock Text="{Binding}" Foreground="#F2D77A" FontSize="18" FontWeight="SemiBold"/>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </Border>
</Window>
```

  Ne pas enregistrer cette fenêtre dans `ViewLocator` (elle est instanciée directement).
- [ ] **Step 3** — Code-behind : dans `OnOpened`, récupérer `TryGetPlatformHandle()?.Handle` et appliquer `SetWindowLongPtr(GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)`.
- [ ] **Step 4** — `IOverlayNotificationService.ShowAsync(SignatureMatch match)` / `ShowMessageAsync(string text, PixelPoint at)` (ce dernier pour les cas "signature non reconnue" — optionnel, derrière un flag). `OverlayNotificationService` : tout sur `Dispatcher.UIThread` ; ferme l'overlay précédent s'il existe ; positionne `Position = new PixelPoint(bounds.X, bounds.Bottom + 4)` ; `Show()` ; `DispatcherTimer` de `ScanConstants.OverlayDurationMs` → `Close()`. S'assurer que la fenêtre reste dans les limites de l'écran (`Screens.ScreenFromPoint`).
- [ ] **Vérification** — Bouton temporaire "Tester l'overlay" dans Paramètres (à garder, il est utile : le brancher sur `ShowAsync` avec un match factice `5× Iron` positionné au centre de l'écran primaire). Contrôler : pas de bordure, pas dans la barre des tâches ni Alt-Tab, le focus reste sur la fenêtre active (tester avec SC au premier plan : on continue de piloter), opacité visible, disparition après 5 s, clics traversants.

---

## Task 6 : Raccourci clavier global

**Files :** `Services/Scan/ScanHotkeySettings.cs`, `Services/Scan/IGlobalHotkeyService.cs`, `Services/Scan/Win32GlobalHotkeyService.cs`

- [ ] **Step 1** — `ScanHotkeySettings` :

```csharp
public sealed record ScanHotkeySettings(bool Enabled, Key Key, KeyModifiers Modifiers)
{
    public static ScanHotkeySettings Default => new(true, Key.F9, KeyModifiers.Control);
    public string ToDisplayString();          // "Ctrl + Shift + F9"
    public static async Task<ScanHotkeySettings> LoadAsync(ISettingsService s);   // Enum.TryParse, fallback Default
    public async Task SaveAsync(ISettingsService s);
}
```

- [ ] **Step 2** — Interface :

```csharp
public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    /// <summary>(Ré)enregistre le raccourci. Retourne false + message si l'OS refuse (déjà utilisé).</summary>
    bool TryApply(ScanHotkeySettings settings, out string? error);
    void Stop();
}
```

- [ ] **Step 3** — `Win32GlobalHotkeyService` : thread dédié (`IsBackground = true`, nom `"StarXelem.Hotkey"`) qui fait `RegisterHotKey(IntPtr.Zero, HotkeyId, mods | MOD_NOREPEAT, vk)` puis boucle `while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { if (msg.message == WM_HOTKEY) HotkeyPressed?.Invoke(...); }`, `UnregisterHotKey` à la sortie. `TryApply` : si un thread tourne, `PostThreadMessage(threadId, WM_QUIT)` + `Join`, puis redémarre ; le résultat de `RegisterHotKey` est transmis via un `ManualResetEventSlim` + champ erreur (`Marshal.GetLastPInvokeError()` → message "Ce raccourci est déjà utilisé par une autre application"). Conversion `Avalonia.Input.Key` → virtual-key : `KeyInterop.VirtualKeyFromKey` n'existe pas en Avalonia ; écrire une table pour les touches supportées par le `HotkeyBox` (F1–F24, A–Z, 0–9, pavé numérique, `Insert/Delete/Home/End/PageUp/PageDown`, `Pause`, `ScrollLock`). Modificateurs : `Control→MOD_CONTROL`, `Alt→MOD_ALT`, `Shift→MOD_SHIFT`.
  - L'événement est levé sur le thread hotkey : l'orchestrateur ne doit rien toucher d'UI sans `Dispatcher.UIThread`.
  - Anti-rebond : ignorer une pression si un scan est déjà en cours (`Interlocked` sur un flag dans l'orchestrateur).
- [ ] **Vérification** — Logger `"Hotkey pressed"` ; avec SC au premier plan, appuyer sur `Ctrl+F9` → la ligne apparaît dans les logs. Changer la touche → l'ancienne ne réagit plus, la nouvelle oui. Simuler un conflit (enregistrer `Ctrl+F9` dans un autre outil) → `TryApply` retourne false avec message.

---

## Task 7 : Orchestrateur et branchement au démarrage

**Files :** `Services/Scan/IScanSignatureOrchestrator.cs`, `Services/Scan/ScanSignatureOrchestrator.cs`, `App.axaml.cs`, `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1** — `ScanSignatureOrchestrator` (singleton) : dépend de `IGlobalHotkeyService`, `IScreenCaptureService`, `ISignatureOcrService`, `IMineralSignatureRepository`, `IOverlayNotificationService`, `ISettingsService`, `ILogger`.

```csharp
public interface IScanSignatureOrchestrator
{
    Task StartAsync();                       // charge les settings, applique le hotkey si Enabled
    Task ApplySettingsAsync(ScanHotkeySettings settings);   // appelé par la page Paramètres
    Task RunOnceAsync();                     // pipeline complet (aussi utilisable par le bouton "Tester")
    string? LastError { get; }
}
```

  `RunOnceAsync` :
  1. `if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;`
  2. `Stopwatch` ; capture → si null, log + retour.
  3. OCR → candidats ; si vide, log `"Aucune signature détectée"` (et overlay "Aucune signature" si option activée).
  4. Pour chaque candidat (ordre fourni), `FindBySignatureAsync(value, SignatureTolerance)` ; au premier non vide → `overlay.ShowAsync(new SignatureMatch(candidate, rows))`, log durée totale, break.
  5. Si aucun match : log `"Signature {v} inconnue"`.
  6. `finally` : `frame.Dispose()`, `_running = 0`.
  Formatage des lignes : `$"{row.ClusterSize}× {row.MineralName}"` ; trier par écart puis `ClusterSize`.
- [ ] **Step 2** — DI : enregistrer en singleton `IScreenCaptureService`, `ISignatureOcrService`, `IGlobalHotkeyService`, `IOverlayNotificationService`, `IScanSignatureOrchestrator` (versions `Design*` inertes en mode design : pas de P/Invoke, `IsAvailable = false`).
- [ ] **Step 3** — `App.OnFrameworkInitializationCompleted` : après la création de `desktop.MainWindow`, `_ = Services.GetRequiredService<IScanSignatureOrchestrator>().StartAsync();` et sur `desktop.ShutdownRequested` (ou `Exit`), `IGlobalHotkeyService.Stop()`.
- [ ] **Step 4** — Retirer les commandes/boutons temporaires des tâches 3 et 4 (garder "Tester l'overlay" / "Tester maintenant" de la page Paramètres).
- [ ] **Vérification (bout en bout)** — SC en borderless devant un cluster scanné : `Ctrl+F9` → en < 1 s, l'overlay `N× Minéral` apparaît sous la valeur de signature, disparaît après 5 s, SC garde le focus et les commandes. Tester aussi avec le HUD décalé (head tracking) : l'overlay suit la position lue. Logs : durée capture / OCR / BDD.

---

## Task 8 : Page Paramètres

**Files :** `Components/HotkeyBox.cs`, `ViewModels/SettingsTabViewModel.cs`, `Views/SettingsTabView.axaml`

- [ ] **Step 1** — `Components/HotkeyBox` : `TextBox` en lecture seule, `Focusable`, qui sur `KeyDown` (hors touches de modificateur seules) capture `e.Key` + `e.KeyModifiers`, expose deux `StyledProperty` (`Key`, `Modifiers`) et affiche `"Ctrl + Shift + F9"`. `Escape` annule, `Backspace` vide. Ne retenir que les touches de la table de conversion de la tâche 6 (sinon ignorer et afficher un tooltip "Touche non supportée").
- [ ] **Step 2** — `SettingsTabViewModel` : injecter `IScanSignatureOrchestrator` et `ISignatureOcrService` ; propriétés `[ObservableProperty]` `ScanEnabled`, `ScanKey` (`Key`), `ScanModifiers` (`KeyModifiers`), `ScanError`, `IsOcrAvailable`, `OcrUnavailableReason` ; chargement dans `OnShowAsync` via `ScanHotkeySettings.LoadAsync` ; commandes `SaveScanHotkeyAsync` (sauve + `ApplySettingsAsync`, affiche `Saved` 2 s ou `ScanError`), `TestScanAsync` (→ `RunOnceAsync`), `TestOverlayAsync`. Un modificateur au minimum est requis (validation : sinon `ScanError = "Choisissez au moins un modificateur (Ctrl, Alt ou Shift)."`).
- [ ] **Step 3** — `SettingsTabView.axaml` : sous la carte "Clé API", nouvelle section `TextBlock.section-label "SCAN DE SIGNATURE"` et une carte au même style (icône `ic:SymbolIcon Symbol="Scan"`), contenant :
  - `ToggleSwitch` "Activer le raccourci de scan" (`ScanEnabled`) ;
  - `HotkeyBox` + texte d'aide "Cliquez puis appuyez sur la combinaison (au moins un modificateur). Le raccourci fonctionne même quand Star Citizen a le focus ; il n'est pas transmis au jeu." ;
  - bandeau d'avertissement si `!IsOcrAvailable` (pack de langue anglais manquant) ;
  - note "Le jeu doit être en mode fenêtré sans bordure." ;
  - boutons `btn-save` "Sauvegarder", secondaires "Tester maintenant" et "Tester l'overlay" ;
  - `TextBlock` rouge `ScanError` (`IsVisible` si non vide).
- [ ] **Step 4** — Utiliser le skill `starxelem-take-screenshot` sur l'onglet Paramètres pour vérifier visuellement la carte (alignements, thème sombre/clair).
- [ ] **Vérification** — Modifier le raccourci → sauvegarder → relancer l'application → le raccourci persiste (clé `HKCU\Software\StarXelem\ScanHotkeyKey`). Désactiver → la touche ne réagit plus.

---

## Task 9 : Finitions

- [ ] Documenter la fonctionnalité dans `CLAUDE.md` (section Key Abstractions : nouveaux services `Services/Scan/*`, `Services/Mining/*`) et mettre à jour `Documents/Datafiles/MineableRocks.md` avec un renvoi vers `MineralSignatures.md`.
- [ ] Persister via `memories add` (type `decision`) : choix Windows.Media.Ocr, hotkey `RegisterHotKey` sur thread dédié, hypothèse N × base et son statut de validation.
- [ ] Ajouter `private/scan-samples/` au `.gitignore` si ce n'est pas déjà couvert par `private/`.
- [ ] Vérifier l'empreinte mémoire : les bitmaps de capture sont bien disposés (`Dispose`) après chaque scan ; pas de fuite après 20 scans consécutifs (observer le process dans le Gestionnaire des tâches).

---

## Risques et points d'attention

| Risque | Mitigation |
|---|---|
| Formule N × base fausse (cf. `80,000`) | Générateur isolé (tâche 2, step 5) ; tolérance configurable ; validation en jeu avant tâche 2 |
| OCR Windows indisponible (pas de pack `en`) | Détection au démarrage + message dans Paramètres ; fallback Tesseract possible plus tard |
| Jeu en plein écran exclusif | Documenté dans l'UI ; capture du moniteur en fallback, mais overlay invisible |
| Écrans HiDPI / scaling ≠ 100 % | Travailler exclusivement en pixels physiques (GDI + `PixelPoint`/`PixelRect` Avalonia) ; tester à 125 %/150 % |
| Raccourci déjà pris | `TryApply` remonte l'erreur ; l'utilisateur choisit une autre combinaison |
| Avalonia `ShowActivated=false` insuffisant seul | Doublé par `WS_EX_NOACTIVATE` via `SetWindowLongPtr` |
| Changement de TFM vers `-windows` | Le projet est déjà Windows-only (`Microsoft.Win32.Registry`, `WinExe`) ; vérifier que le `publish` existant (`AppxManifest`, `priconfig`) build toujours |
| Collisions de signatures (4 cas) | Afficher toutes les lignes correspondantes dans l'overlay |

---

## Annexe A — Table de référence des signatures (base × N, N = 1..10)

Signatures de base lues dans le p4k (LIVE 4.10, cf. `Documents/Datafiles/MineableRocks.md`). Cette table est celle qui sera générée en base ; elle est fournie ici pour référence et vérification.

| Minéral | Rareté | Base | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Savrilium | Legendary | 3200 | 3200 | 6400 | 9600 | 12800 | 16000 | 19200 | 22400 | 25600 | 28800 | 32000 |
| Quantainium | Legendary | 3170 | 3170 | 6340 | 9510 | 12680 | 15850 | 19020 | 22190 | 25360 | 28530 | 31700 |
| Stileron | Legendary | 3185 | 3185 | 6370 | 9555 | 12740 | 15925 | 19110 | 22295 | 25480 | 28665 | 31850 |
| Lindinium | Epic | 3400 | 3400 | 6800 | 10200 | 13600 | 17000 | 20400 | 23800 | 27200 | 30600 | 34000 |
| Ouratite | Epic | 3370 | 3370 | 6740 | 10110 | 13480 | 16850 | 20220 | 23590 | 26960 | 30330 | 33700 |
| Riccite | Epic | 3385 | 3385 | 6770 | 10155 | 13540 | 16925 | 20310 | 23695 | 27080 | 30465 | 33850 |
| Bexalite | Rare | 3600 | 3600 | 7200 | 10800 | 14400 | 18000 | 21600 | 25200 | 28800 | 32400 | 36000 |
| Gold | Rare | 3585 | 3585 | 7170 | 10755 | 14340 | 17925 | 21510 | 25095 | 28680 | 32265 | 35850 |
| Borase | Rare | 3570 | 3570 | 7140 | 10710 | 14280 | 17850 | 21420 | 24990 | 28560 | 32130 | 35700 |
| Taranite | Rare | 3555 | 3555 | 7110 | 10665 | 14220 | 17775 | 21330 | 24885 | 28440 | 31995 | 35550 |
| Beryl | Rare | 3540 | 3540 | 7080 | 10620 | 14160 | 17700 | 21240 | 24780 | 28320 | 31860 | 35400 |
| Tungsten | Uncommon | 3870 | 3870 | 7740 | 11610 | 15480 | 19350 | 23220 | 27090 | 30960 | 34830 | 38700 |
| Torite | Uncommon | 3900 | 3900 | 7800 | 11700 | 15600 | 19500 | 23400 | 27300 | 31200 | 35100 | 39000 |
| Agricium | Uncommon | 3885 | 3885 | 7770 | 11655 | 15540 | 19425 | 23310 | 27195 | 31080 | 34965 | 38850 |
| Titanium | Uncommon | 3855 | 3855 | 7710 | 11565 | 15420 | 19275 | 23130 | 26985 | 30840 | 34695 | 38550 |
| Aslarite | Uncommon | 3840 | 3840 | 7680 | 11520 | 15360 | 19200 | 23040 | 26880 | 30720 | 34560 | 38400 |
| Laranite | Uncommon | 3825 | 3825 | 7650 | 11475 | 15300 | 19125 | 22950 | 26775 | 30600 | 34425 | 38250 |
| Iron | Common | 4270 | 4270 | 8540 | 12810 | 17080 | 21350 | 25620 | 29890 | 34160 | 38430 | 42700 |
| Aluminum | Common | 4285 | 4285 | 8570 | 12855 | 17140 | 21425 | 25710 | 29995 | 34280 | 38565 | 42850 |
| Silicon | Common | 4255 | 4255 | 8510 | 12765 | 17020 | 21275 | 25530 | 29785 | 34040 | 38295 | 42550 |
| Copper | Common | 4240 | 4240 | 8480 | 12720 | 16960 | 21200 | 25440 | 29680 | 33920 | 38160 | 42400 |
| Corundum | Common | 4225 | 4225 | 8450 | 12675 | 16900 | 21125 | 25350 | 29575 | 33800 | 38025 | 42250 |
| Quartz | Common | 4210 | 4210 | 8420 | 12630 | 16840 | 21050 | 25260 | 29470 | 33680 | 37890 | 42100 |
| Hephaestanite | Common | 4180 | 4180 | 8360 | 12540 | 16720 | 20900 | 25080 | 29260 | 33440 | 37620 | 41800 |
| Tin | Common | 4195 | 4195 | 8390 | 12585 | 16780 | 20975 | 25170 | 29365 | 33560 | 37755 | 41950 |
| Ice | Common | 4300 | 4300 | 8600 | 12900 | 17200 | 21500 | 25800 | 30100 | 34400 | 38700 | 43000 |

**Collisions (plusieurs interprétations pour une même valeur) :**

| Signature | Interprétations |
|---|---|
| 19200 | 5× Aslarite, 6× Savrilium |
| 28800 | 8× Bexalite, 9× Savrilium |
| 30600 | 8× Laranite, 9× Lindinium |
| 38700 | 9× Ice, 10× Tungsten |

Les minéraux FPS (signature générique 3000) et véhicule terrestre (4000) sont exclus, comme dans l'extraction existante.
