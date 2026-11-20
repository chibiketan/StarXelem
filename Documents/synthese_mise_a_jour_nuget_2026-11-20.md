# Synthèse des packages NuGet — 2026-11-20

Projet analysé : `src/StarXelem/StarXelem.csproj` (le projet `StarXelem.cli.testdb` n'a aucun `PackageReference` propre, il hérite de StarXelem).
Cible : `net10.0`, SDK `10.0.x` (`global.json`).

Sources : `dotnet list package --outdated` + métadonnées NuGet (dépendances de chaque version) + GitHub Advisories.

## 1. Tableau récapitulatif

| Package | Utilisée | Dernier correctif même branche | Dernière disponible | Saut | Remarque |
|---|---|---|---|---|---|
| Avalonia | 11.3.13 | **11.3.23** | 12.1.6 | Majeur | Bloque tout l'écosystème UI |
| Avalonia.Desktop | 11.3.13 | **11.3.23** | 12.1.6 | Majeur | Corrige la vuln. Tmds.DBus en 11.3.23 |
| Avalonia.Themes.Fluent | 11.3.13 | **11.3.23** | 12.1.6 | Majeur | |
| Avalonia.Fonts.Inter | 11.3.13 | **11.3.23** | 12.1.6 | Majeur | |
| Avalonia.Diagnostics | 11.3.13 | **11.3.23** | 11.3.23 | — | **N'existe plus en 12.x** |
| FluentAvaloniaUI | 2.4.1 | **2.5.1** (Avalonia ≥ 11.3.12) | 3.1.0 (Avalonia ≥ 12.1.0) | Majeur | Fournit aussi DataGrid/ColorPicker en transitif |
| FluentIcons.Avalonia.Fluent | 2.0.321 | **2.0.330** | 2.1.345 (FluentAvaloniaUI ≥ 3.0.0) | Mineur mais bloquant | 2.1.x impose Avalonia 12 |
| AsyncImageLoader.Avalonia | 3.7.0 | 3.7.0 (dernière compatible 11) | 4.0.0 | Majeur | **3.8.0 exige déjà Avalonia 12** |
| ClosedXML | 0.105.0 | **0.105.1** | 0.105.1 | Patch | Sans risque |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.9 | **10.0.15** | 11.0.0 | Majeur | **11.0.0 cible uniquement net11.0** |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.9 | **10.0.15** | 11.0.0 | Majeur | 11.0.0 supporte net10.0 |
| Microsoft.Extensions.Logging | 10.0.9 | **10.0.15** | 11.0.0 | Majeur | 11.0.0 supporte net10.0 |
| Microsoft.Extensions.Logging.Console | 10.0.5 | **10.0.15** | 11.0.0 | Majeur | Désaligné avec les autres (10.0.5 vs 10.0.9) |
| Microsoft.Extensions.Logging.Debug | 10.0.5 | **10.0.15** | 11.0.0 | Majeur | Idem |
| System.IO.Hashing | 10.0.5 | **10.0.15** | 11.0.0 | Majeur | 11.0.0 supporte net10.0 |
| CommunityToolkit.Mvvm | 8.4.2 | — | 8.4.2 | — | À jour |
| Humanizer.Core.fr | 3.0.10 | — | 3.0.10 | — | À jour |
| OneOf | 3.0.271 | — | 3.0.271 | — | À jour |

Hors NuGet : `Google.Protobuf`, `Grpc.*` et `StarBreaker.*` sont des DLL locales (`libs/`), non couvertes par cette analyse.

## 2. Vulnérabilités signalées à la restauration

| Package transitif | Version | Avis | Amené par | Correction |
|---|---|---|---|---|
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.11 | GHSA-2m69-gcr7-jv3q (élevée, SQLite embarqué vulnérable, versions ≤ 2.1.11) | EF Core Sqlite 10.0.9 | EF Core Sqlite **10.0.15** tire SQLitePCLRaw 2.1.13 |
| Tmds.DBus.Protocol | 0.21.2 | GHSA-xrw6-gwf8-vvr9 (élevée, D-Bus : usurpation de signaux / DoS) | Avalonia.FreeDesktop 11.3.13 | Avalonia **11.3.23** tire Tmds.DBus.Protocol 0.21.3 |

Les deux vulnérabilités se corrigent **sans changement de version majeure**. Tmds.DBus ne concerne en pratique que Linux (D-Bus), mais l'avertissement reste visible au build.

## 3. Incompatibilités et chaînes de dépendances

### 3.1 Bloc Avalonia 11 → 12 (indissociable)

Les packages UI doivent migrer **ensemble** : impossible de monter l'un sans les autres.

```
Avalonia 12.x
 ├─ FluentAvaloniaUI 3.x          (3.0.0 → Avalonia ≥ 12.0.0 ; 3.1.0 → Avalonia ≥ 12.1.0, net10.0 uniquement)
 │    └─ FluentIcons.Avalonia.Fluent 2.1.x   (exige FluentAvaloniaUI ≥ 3.0.0)
 └─ AsyncImageLoader.Avalonia ≥ 3.8.0 / 4.0.0   (exige Avalonia ≥ 12.0.0)
```

Points de vigilance identifiés dans le code :

- **Avalonia.Diagnostics** n'a pas de version 12 (dernière : 11.3.23). Avalonia 12 s'appuie sur les nouveaux *Developer Tools* (`AvaloniaUI.DiagnosticsSupport`, actuellement 2.2.3). Aucun appel à `AttachDevTools()` n'a été trouvé dans le code : le package semble inutilisé et peut probablement être simplement supprimé.
- **DataGrid** : utilisé dans 7 vues (`ItemsTabView`, `ShipTabView`, `FpsWeaponsTabView`, `FriendListTabView`, `ContainerTabView`, `ItemComparisonPopupContentView`…) mais **non référencé explicitement** : il arrive en transitif via FluentAvaloniaUI. En 12.x, `Avalonia.Controls.DataGrid` suit sa propre numérotation (dernière : 12.1.2, distincte d'Avalonia 12.1.6). Il est conseillé de l'ajouter en référence directe lors de la migration.
- **FluentAvaloniaUI** : utilisé dans ~15 fichiers (`ui:ProgressRing`, `ui:SymbolIcon`, `App.axaml`…). Le passage en 3.x est une version majeure : vérifier les styles/thèmes déclarés dans `App.axaml` et les contrôles utilisés.
- **Propriété `Watermark`** des `TextBox` (11 occurrences, dont `Components/ColumnHeader.axaml`) : Avalonia 12 privilégie `PlaceholderText` ; à vérifier dans le guide de migration Avalonia 12 (renommage/obsolescence).
- Le guide officiel de migration Avalonia 11 → 12 devra être suivi (changements sur les bindings compilés, les thèmes, les API de plateforme). Toute la refonte graphique en cours (styles `SxTheme.axaml`, `BlueprintStyles.axaml`) est exposée.

### 3.2 Bloc .NET 10 → 11

- **Microsoft.EntityFrameworkCore.Sqlite 11.0.0 ne cible que `net11.0`** : incompatible avec le projet actuel (`net10.0`). Monter EF Core 11 impose de passer le `TargetFramework` à `net11.0` (dans `Directory.Build.props` **et** dans les deux `.csproj`, qui le redéfinissent) ainsi que le `global.json` (SDK 11).
- EF Core 11 tire aussi **SQLitePCLRaw 3.x** (3.0.5) au lieu de 2.1.x : changement majeur du binding SQLite natif.
- `Microsoft.Extensions.*` 11.0.0 et `System.IO.Hashing` 11.0.0 fournissent une cible `net10.0` : installables techniquement sur net10, **mais** EF Core 10.x dépend de `Microsoft.Extensions.Logging`/`Caching.Memory` 10.x ; mélanger Logging 11 et EF Core 10 provoque une montée de version transitive non testée par Microsoft. Recommandation : garder toute la famille Microsoft alignée sur la même version (10.0.15 maintenant, 11.0.x avec net11).
- .NET 10 est une version LTS (supportée jusqu'en novembre 2028) ; .NET 11 est une STS. Rester en 10.0.x est un choix tout à fait légitime.

### 3.3 Indépendants

- **ClosedXML 0.105.1** : patch, aucune dépendance modifiée.
- **CommunityToolkit.Mvvm, Humanizer.Core.fr, OneOf** : déjà à jour.

## 4. Recommandation de séquencement

1. **Mise à jour sans risque (immédiate)** — corrige les deux vulnérabilités, aucune modification de code attendue :
   - Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Diagnostics → **11.3.23**
   - FluentAvaloniaUI → **2.5.1** ; FluentIcons.Avalonia.Fluent → **2.0.330**
   - Toute la famille Microsoft (EF Core Sqlite, Extensions.*, System.IO.Hashing) → **10.0.15** (réaligne aussi Console/Debug)
   - ClosedXML → **0.105.1**
2. **Migration Avalonia 12 (chantier dédié)** — Avalonia 12.1.x + FluentAvaloniaUI 3.1.0 + FluentIcons 2.1.345 + AsyncImageLoader 4.0.0, suppression d'Avalonia.Diagnostics, ajout explicite d'`Avalonia.Controls.DataGrid`. Nécessite une revue visuelle de tous les écrans.
3. **Migration .NET 11 (optionnelle)** — `net11.0` + SDK 11 + EF Core 11 / Extensions 11. Indépendante d'Avalonia 12 (Avalonia 12.1.6 cible net8.0/net10.0, donc compatible net11).
