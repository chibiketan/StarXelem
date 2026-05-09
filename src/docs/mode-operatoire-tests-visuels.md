# Framework de Tests Visuels — Mode Opératoire

## Vue d'ensemble

Le framework se compose de trois couches complémentaires :

```
┌─────────────────────────────────────────────────────────┐
│  ReportCLI (outil CLI autonome)                         │
│  └── Comparaison ad-hoc entre deux images               │
├─────────────────────────────────────────────────────────┤
│  VisualJudge (bibliothèque d'analyse)                   │
│  ├── PixelDiffEngine    → diff pixel par pixel          │
│  ├── OllamaVisualJudge  → analyse sémantique via LLaVA  │
│  └── HtmlReportGenerator→ rapport HTML autonome         │
├─────────────────────────────────────────────────────────┤
│  Tests Visuels (suite xUnit headless)                   │
│  ├── HeadlessAppFixture   → application sans affichage  │
│  ├── VisualPilot          → navigation programmatique   │
│  ├── ReferenceImageGen    → capture via Playwright      │
│  └── ScreenshotHelper     → capture d'écran Avalonia    │
└─────────────────────────────────────────────────────────┘
```

---

## 1. Lancer la suite de tests complète

### Commande de base

```bash
dotnet test StarXelem.Tests.Visual --no-restore
```

Cela exécute tous les 76 tests (headless, navigation, pilotage, comparaison visuelle).

### Options utiles

| Option | Effet |
|--------|-------|
| `--filter "FullyQualifiedName~PilotNavigation"` | Uniquement les tests de pilotage |
| `--filter "FullyQualifiedName~VisualComparison"` | Uniquement la comparaison visuelle |
| `--filter "DisplayName~Can_Click_LoadButton"` | Un test précis par nom |
| `-v detailed` | Sortie verbale complète |

### Dimensions des captures

Toutes les captures se font à **1920×1080** par défaut. Pour changer cette taille :

```bash
# PowerShell
$env:SCREENSHOT_SIZE = "1366x768"
dotnet test StarXelem.Tests.Visual --no-restore

# Bash
export SCREENSHOT_SIZE="1366x768"
dotnet test StarXelem.Tests.Visual --no-restore
```

### Où se trouvent les résultats

Après exécution, les fichiers sont générés dans `StarXelem.Tests.Visual/bin/Debug/net10.0/` :

| Dossier | Contenu |
|---------|---------|
| `screenshots/` | Captures d'écran de chaque test (PNG) |
| `screenshots/References/` | Images de référence générées par Playwright |
| `reports/report_YYYYMMDD_HHMMSS/` | Rapports HTML de comparaison |

---

## 2. Utiliser l'outil ReportCLI

L'outil CLI permet une comparaison rapide entre deux images, en dehors du framework xUnit.

### Construction et publication

```bash
# Build simple (exécutable dépendant du runtime .NET)
dotnet build StarXelem/ReportCLI/StarXelem.ReportCLI.csproj -c Release

# Publication AOT autonome (binaire unique, ~50 Mo)
dotnet publish StarXelem/ReportCLI/StarXelem.ReportCLI.csproj -c Release --self-contained false
```

Le binaire se trouve dans `ReportCLI/bin/Release/net10.0/publish/stx-report.exe`.

### Syntaxe de la commande

```bash
stx-report <ACTUAL> <REFERENCE> [options]
```

| Argument / Option | Description | Défaut |
|-------------------|-------------|--------|
| `<ACTUAL>` | Chemin vers le screenshot réel (obligatoire) | — |
| `<REFERENCE>` | Chemin vers l'image de référence (obligatoire) | — |
| `-n, --name` | Nom de la page pour l'analyse sémantique | `Comparison` |
| `-o, --output` | Dossier de sortie des rapports et heatmaps | `.` |
| `--no-heatmap` | Désactive la génération de la heatmap | — |
| `-e, --endpoint` | URL personnalisée du serveur Ollama | `http://localhost:11434` |

### Exemples concrets

```bash
# Comparaison simple avec analyse complète
stx-report screenshots/tab_Extractions.png screenshots/References/extractions_screen_dark.png -n "Extractions"

# Sans heatmap (plus rapide)
stx-report actual.png reference.png --no-heatmap

# Avec un serveur Ollama distant
stx-report actual.png reference.png -e http://mon-srv:11434 -o rapports/

# Sortie dans un dossier spécifique
stx-report actual.png reference.png -n "MonHangar" -o D:/rapports-tests
```

### Codes de sortie

| Code | Signification |
|------|---------------|
| 0 | Conforme (compliant) |
| 1 | Image réelle manquante |
| 2 | Image de référence manquante |
| 3 | Non conforme — écarts détectés |

---

## 3. VisualPilot — navigation headless

`VisualPilot` est la classe centrale pour interagir avec l'application sans affichage. Elle s'utilise dans les tests xUnit mais aussi dans tout code C# qui a accès au fixture.

### Ouverture de l'application

```csharp
var window = await VisualPilot.OpenAppAsync(fixture);
// La fenêtre est redimensionnée à 1920x1080 automatiquement.
```

### Navigation entre onglets

Les noms d'onglet correspondent aux valeurs `Name` définies dans le MainWindowViewModel :

```csharp
VisualPilot.NavigateToTab(window, "Amis");
VisualPilot.NavigateToTab(window, "Mon hangar");
VisualPilot.NavigateToTab(window, "Blueprints");
VisualPilot.NavigateToTab(window, "Objets");
VisualPilot.NavigateToTab(window, "Loadout vaisseaux");
VisualPilot.NavigateToTab(window, "Missions");
VisualPilot.NavigateToTab(window, "Extractions");
VisualPilot.NavigateToTab(window, "Paramètres");
```

### Capture d'écran

```csharp
// Enregistre dans screenshots/ au format PNG
string path = window.CaptureScreenshot("mon_ecran.png");
```

### Interaction avec les contrôles

Chaque contrôle interactif possède un attribut `Name` dans le XAML. Le pilot utilise ces noms pour les localiser :

```csharp
// Trouver un bouton par son nom et cliquer dessus
var pageContent = window.GetActivePageContent();
pageContent.ClickButton("LoadButton");

// Cocher/décocher un toggle
var friendPage = viewModel.Pages.OfType<FriendListTabViewModel>().First();
friendPage.OnlyConnected = !friendPage.OnlyConnected;
```

### Attendre la fin du chargement

```csharp
var vm = (MainWindowViewModel)window.DataContext!;
await VisualPilot.WaitForLoadAsync(vm.CurrentPage); // timeout 10s par défaut
```

---

## 4. Générer des images de référence

Le `ReferenceImageGenerator` utilise Playwright pour ouvrir des maquettes HTML dans un navigateur headless et capturer des screenshots de référence.

### Installation de Playwright

La première fois, installez Chromium :

```bash
# Via le package .NET (recommandé)
cd StarXelem.Tests.Visual
npx playwright install chromium

# Ou via la commande du package NuGet
dotnet tool restore
```

### Utilisation dans un test

```csharp
using var generator = new ReferenceImageGenerator();

// Capture d'une maquette HTML complète
string path1 = await generator.CaptureFromHtml(
    "D:/repos/.../maquettes/extractions_screen.html", "_dark");

// Capture d'un élément spécifique par sélecteur CSS
string path2 = await generator.CaptureElement(
    "D:/repos/.../maquettes/connection_status_bar.html",
    "#status-bar-dark",   // sélecteur CSS
    "connexion_bar_dark"  // nom du fichier de sortie
);
```

Les images sont sauvegardées dans `screenshots/References/`.

---

## 5. Intégrer Ollama pour l'analyse sémantique

L'analyse sémantique compare deux images via un modèle LLaVA local. Elle est **optionnelle** — le framework fonctionne sans, en tombant sur le seul pixel diff.

### Prérequis

1. Installer Ollama : https://ollama.ai
2. Tirer le modèle LLaVA 7b :

```bash
ollama pull llava:7b
```

3. Vérifier que le serveur répond :

```bash
curl http://localhost:11434/api/generate -d '{
  "model": "llava:7b",
  "prompt": "hello",
  "stream": false
}'
```

### Utilisation directe de l'API

```csharp
using StarXelem.VisualJudge;

var result = await OllamaVisualJudge.CompareAsync(
    actualImagePath: "screenshots/tab_Extractions.png",
    referenceImagePath: "screenshots/References/extractions_screen_dark.png",
    pageName: "Extractions"
);

if (result.IsSkipped)
{
    Console.WriteLine(result.Summary); // "Ollama non disponible..."
}
else
{
    Console.WriteLine($"Score : {result.Score:P2}");
    foreach (var gap in result.Gaps)
        Console.WriteLine($"  [{gap.Severity}] {gap.Category}: {gap.Description}");
}
```

### Serveur Ollama distant

Si Ollama tourne sur une autre machine, passez l'endpoint :

```csharp
await OllamaVisualJudge.CompareAsync(actual, reference, "Nom", endpoint: "http://192.168.1.50:11434");
```

---

## 6. Vivre avec le framework — ajouter de nouveaux écrans

Quand un nouvel écran est ajouté à l'application, voici la procédure pour le couvrir par les tests visuels.

### Étape 1 : nommer les contrôles dans le XAML

Ajoutez des attributs `Name` aux contrôles interactifs clés du nouveau fichier `.axaml` :

```xml
<!-- Exemple pour un nouvel onglet -->
<Button Name="LoadButton" Content="Charger" ... />
<DataGrid Name="MyDataGrid" ... />
<ToggleSwitch Name="OnlyConnectedToggle" ... />
<TextBox Name="SearchField" ... />
```

**Règles de nommage :**

| Élément | Nom à utiliser |
|---------|---------------|
| Bouton charger/actualiser | `LoadButton` (uniformisé) |
| DataGrid principal | `{NomOnglet}DataGrid` |
| Champ recherche | `SearchField` |
| Toggle de filtrage | `{NomFiltre}` |

### Étape 2 : vérifier que le pilot détecte l'onglet

Ajoutez le nom de l'onglet dans la liste du test `PilotNavigationTest.Can_Navigate_And_Capture_Each_Tab`. Le test existant itère sur un tableau de noms — ajoutez simplement votre nouvel onglet :

```csharp
string[] tabNames = {
    "Mon hangar", "Objets", "Blueprints", "Amis",
    "Loadout vaisseaux", "Missions", "Extractions", "Paramètres",
    "NomDeVotreNouvelOnglet"  // ← ajout ici
};
```

### Étape 3 : écrire des tests de pilotage spécifiques

Créez une classe de test dans `StarXelem.Tests.Visual/` si le nouvel écran a un comportement interactif notable (chargement, filtrage, etc.) :

```csharp
using Avalonia.Headless.XUnit;

namespace StarXelem.Tests.Visual;

public class MonNouvelEcranTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public MonNouvelEcranTest(HeadlessAppFixture fixture) => _fixture = fixture;
    public void Dispose() { }

    [AvaloniaFact]
    public async Task Can_Load_MonNouvelEcran()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);
        VisualPilot.NavigateToTab(window, "NomDeVotreNouvelOnglet");

        // Attendre le chargement si l'onglet a un état IsLoading
        var vm = (MainWindowViewModel)window.DataContext!;
        if (vm.CurrentPage != null)
            await VisualPilot.WaitForLoadAsync(vm.CurrentPage);

        // Capture pour vérification visuelle
        string path = window.CaptureScreenshot("mon_nouvel_ecran.png");
        Assert.True(File.Exists(path));

        // Vérifiez que le bouton charger est accessible
        var pageContent = window.GetActivePageContent();
        Assert.NotNull(pageContent);
        bool clicked = pageContent!.ClickButton("LoadButton");
        Assert.True(clicked, "Le bouton LoadButton n'a pas été trouvé.");

        await Task.Delay(300); // temps pour le mock de répondre

        window.Close();
    }
}
```

### Étape 4 : créer une image de référence (optionnel)

Si vous avez une maquette HTML pour le nouvel écran, générez la référence :

```csharp
using var gen = new ReferenceImageGenerator();
string refPath = await gen.CaptureFromHtml(
    "D:/.../maquettes/nouvel_ecran.html", "_dark");
```

Sinon, capturez un screenshot de l'application réelle et utilisez-le comme référence initiale :

1. Lancez les tests — le screenshot réel est dans `screenshots/`
2. Copiez-le vers `screenshots/References/` avec un nom clair
3. Le test de comparaison visuelle le trouvera automatiquement

### Étape 5 : ajouter à la comparaison globale

Le test `VisualComparisonTest.All_Tabs_Comparison_Report` itère sur tous les onglets connus. Ajoutez votre nouvel onglet au tableau `tabNames` :

```csharp
string[] tabNames = {
    "Mon hangar", "Objets", "Blueprints", "Amis",
    "Loadout vaisseaux", "Missions", "Extractions", "Paramètres",
    "NomDeVotreNouvelOnglet"  // ← ajout ici
};
```

---

## 7. Pipeline CI/CD recommandé

Pour intégrer ces tests dans un pipeline d'intégration continue :

```yaml
# Exemple GitHub Actions (à adapter)
- name: Run visual tests
  run: dotnet test StarXelem.Tests.Visual --no-restore -v detailed

- name: Upload screenshots on failure
  if: failure()
  uses: actions/upload-artifact@v4
  with:
    name: screenshot-failures
    path: |
      **/screenshots/*.png
      **/reports/**/*.html
```

**Note :** le pixel diff fonctionne sans Ollama. L'analyse sémantique est marquée `IsSkipped` si le serveur n'est pas disponible, et le test passe en se basant sur le seuil de 95 % de similitude pixel par pixel.

---

## 8. Récapitulatif des projets du framework

| Projet | Rôle | Fichier .csproj |
|--------|------|-----------------|
| `StarXelem.Tests.Visual` | Suite xUnit : tests headless, pilotage, comparaison | `StarXelem.Tests.Visual/StarXelem.Tests.Visual.csproj` |
| `StarXelem.VisualJudge` | Bibliothèque d'analyse : pixel diff, Ollama, rapports HTML | `StarXelem.VisualJudge/StarXelem.VisualJudge.csproj` |
| `StarXelem.ReportCLI` | Outil CLI autonome pour comparaison ad-hoc | `StarXelem/ReportCLI/StarXelem.ReportCLI.csproj` |
