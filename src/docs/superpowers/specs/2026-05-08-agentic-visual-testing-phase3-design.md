---
name: Agentic Visual Testing Framework — Phase 3 Design
description: Intelligence & Reporting : juge visuel via LLaVA local, diff pixel par pixel, et rapports HTML autonomes.
type: design
---

# Design Spec: Agentic Visual Testing Framework — Phase 3 (Intelligence & Reporting)

## Contexte

Les Phases 1 et 2 sont terminées avec 51/51 tests passant :
- **Phase 1** : Infrastructure headless Avalonia, DI container en mode test, captures d'écran via Skia.
- **Phase 2** : Pilot (`VisualPilot`) pour naviguer dans l'UI et capturer chaque onglet ; générateur de références Playwright pour les maquettes HTML.

La Phase 3 introduit deux capacités : un **juge visuel** capable d'interpréter les écarts entre le rendu réel et la référence, et un **système de rapport HTML** autonome par test.

## Architecture

### Vue d'ensemble

```
┌─────────────┐         ┌──────────────┐
│   Agent A   │         │    Agent B   │
│  Le Pilot   │         │  Référence   │
│  (Phase 1)  │         │  (Phase 2)   │
│             │         │              │
│ Avalonia    │         │ Playwright   │
│ Headless    │         │ HTML → PNG   │
└──────┬──────┘         └──────┬───────┘
       │                       │
       │  actual.png           │  reference.png
       ▼                       ▼
┌──────────────────────────────────────────┐
│              Agent C                      │
│          Le Juge Visuel                   │
│                                          │
│   LLaVA via Ollama (local)               │
│   Compare les deux images sémantiquement  │
└──────────────────────┬───────────────────┘
                       │
                       ▼
      ┌────────────────────────────────────┐
      │       Rapport HTML Final            │
      │                                     │
      │  Dashboard + Galerie côte-à-côte    │
      │  + Heatmap diff pixel              │
      │  + Analyse sémantique de l'LLM     │
      └─────────────────────────────────────┘
```

### Décisions techniques

| Décision | Choix | Justification |
|----------|-------|---------------|
| Moteur LLM | Ollama + LLaVA 7b | 100% local, reproductible, bon pour les UI structurées |
| Wrapper .NET | OllamaSharp | Typé, async natif, gère les images en base64 proprement |
| Manipulation d'images | SixLabors.ImageSharp | Cross-platform, pas de dépendance GDI+, parfait pour le diff pixel et la heatmap |
| CLI | Spectre.Console.Cli | Aide intégrée, coloration du terminal, tableaux formatés |
| Rapports | HTML autonome par test | Chaque comparaison produit son propre fichier partageable |

## Organisation des projets

```
StarXelem/
├── StarXelem.Tests.Visual/          ← projet de tests existant (Phase 1-2)
│   ├── VisualComparisonTest.cs       ← tests xUnit avec intégration Agent C
│   └── ...
│
├── StarXelem.VisualJudge/            ← NOUVEAU : bibliothèque partagée
│   ├── OllamaVisualJudge.cs          ← orchestration LLM (OllamaSharp)
│   ├── PixelDiffEngine.cs            ← diff pixel par pixel + heatmap
│   └── HtmlReportGenerator.cs        ← génération des rapports HTML
│
├── StarXelem.ReportCLI/              ← NOUVEAU : outil de ligne de commande
│   └── Program.cs                    ← point d'entrée CLI pour générer les rapports
│
└── maquettes/                        ← références Playwright (Phase 2)
    └── *.html
```

## Composants détaillés

### 1. Redimensionnement de la fenêtre Avalonia

**Problème :** Playwright capture à 1920×1080, mais Avalonia headless rend à taille naturelle (~1024px). Impossible de faire un diff pixel par pixel sans dimensions identiques.

**Solution :** Forcer `window.Width = 1920` et `window.Height = 1080` dans `VisualPilot.OpenAppAsync`, puis appeler `window.Arrange()` avec ces dimensions avant toute capture.

### 2. OllamaVisualJudge

Classe principale qui orchestre la comparaison sémantique via LLaVA.

**API :**
```csharp
public class OllamaVisualJudge : IAsyncDisposable
{
    public static async Task<ComparisonResult> CompareAsync(
        string actualImagePath,
        string referenceImagePath,
        string pageName);
}

public record ComparisonResult(
    bool IsCompliant,
    double PixelSimilarityPercent,
    string SemanticAnalysis,
    string HeatmapPath);
```

**Flux :**
1. Charger les deux images en base64
2. Construire un prompt structuré demandant à LLaVA de comparer l'image réelle avec la référence et de retourner un verdict JSON structuré
3. Envoyer via OllamaSharp `ChatAsync` au modèle `llava:7b`
4. Parser la réponse JSON pour extraire le verdict, les écarts détectés et l'explication textuelle

**Prompt système :**
```
Tu es un expert en validation d'interfaces graphiques. Compare deux captures d'écran :
- Image 1 (référence) : le design attendu
- Image 2 (réel) : ce que l'application produit actuellement

Retourne un JSON avec cette structure exacte :
{
  "is_compliant": true/false,
  "score": 0.0-1.0,
  "gaps": [
    { "category": "color|layout|typography|content|missing_element", "description": "...", "severity": "critical|minor" }
  ],
  "summary": "résumé en une phrase"
}
```

### 3. PixelDiffEngine

Calcule un diff pixel par pixel entre deux images de même dimension et produit une heatmap colorisée.

**API :**
```csharp
public static class PixelDiffEngine
{
    public static double SimilarityPercent(string pathA, string pathB);
    // Retourne le pourcentage de pixels identiques (0-100)

    public static async Task<string> GenerateHeatmapAsync(
        string pathA, string pathB, string outputPath);
    // Produit une image où les pixels différents sont surlignés en rouge semi-transparent
}
```

**Implémentation :** Utilise SixLabors.ImageSharp pour charger les deux images en `Rgba32`, parcourt pixel par pixel et calcule la différence de distance euclidienne. Les pixels dont la distance dépasse un seuil (par défaut 30 sur 255) sont marqués en rouge (`#80FF0000`) dans l'image de sortie.

### 4. HtmlReportGenerator

Génère un rapport HTML autonome par test, incluant :
- Dashboard résumé avec statut PASS/FAIL et score de similarité pixel
- Galerie côte-à-côte : référence vs réel
- Heatmap du diff pixel
- Analyse sémantique de l'LLM avec les écarts listés

**API :**
```csharp
public static class HtmlReportGenerator
{
    public static string Generate(ComparisonResult result, string outputPath);
}
```

Le rapport est un fichier HTML unique avec CSS inline (pas de dépendance externe). Les images sont intégrées en base64 pour le partage facile.

### 5. StarXelem.ReportCLI

Outil de ligne de commande qui permet de lancer des comparaisons visuelles sans passer par les tests xUnit.

**Commandes :**
```bash
# Comparer deux images et générer un rapport HTML
dotnet run -- compare actual.png reference.html --page-name "Extractions"

# Scanner un dossier de captures et comparer avec les références existantes
dotnet run -- scan screenshots/actual --ref-dir screenshots/References --output reports/
```

**Sortie terminal :** Spectre.Console affiche un tableau résumé avec statut, similarité pixel et lien vers le rapport HTML.

## Flux de données complet

```
Tests xUnit → VisualPilot (capture actual.png à 1920×1080)
       │
       ├──→ PixelDiffEngine.Compare(actual, reference)
       │     ├── Retourne : similarityPercent + heatmap.png
       │
       └──→ OllamaVisualJudge.Compare(actual, reference)
             ├── Envoie les deux images à LLaVA via OllamaSharp
             └── Retourne : verdict PASS/FAIL + explication textuelle

       → HtmlReportGenerator.Generate(result) → rapport HTML autonome
```

## Tests de validation

- `OllamaVisualJudge` avec des images identiques doit retourner `is_compliant: true` et score ~1.0
- `PixelDiffEngine.SimilarityPercent` avec deux images identiques doit retourner 100%
- `HtmlReportGenerator` doit produire un fichier HTML valide et non vide

## Prérequis d'environnement

- Ollama installé et en cours d'exécution sur `localhost:11434`
- Modèle `llava:7b` téléchargé (`ollama pull llava:7b`)
- Le test détecte l'absence de connexion à Ollama et se marque comme skipped plutôt que failed
