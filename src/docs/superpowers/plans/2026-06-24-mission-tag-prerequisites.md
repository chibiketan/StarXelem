# Mission Tag Prerequisites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter aux missions en base de données les prérequis en termes de tags (`ContractPrerequisite_CompletedContractTags`) ainsi que les tags attribués à la complétion (`ContractResult_CompletionTags`), liés structurellement à la table `Tags` existante.

**Architecture:** Deux nouvelles entités EF Core (`MissionRequiredTagEntity` et `MissionCompletionTagEntity`) sont ajoutées en `Entities.cs` et enregistrées dans le `DbContext`. Le parsing est intégré dans `LocalDatabaseService.cs` : les prérequis sont parsés dans `ProcessContractForDb` (via `contract.additionalPrerequisites`), et les tags de complétion sont parsés dans le case `ContractResult_CompletionTags` de `ProcessMissionRewards`. La BDD est reconstruite entièrement à chaque `RebuildDbAsync` (pas de migration incrémentale).

**Tech Stack:** C# 12, Entity Framework Core (SQLite), `StarBreaker.DataCoreGenerated` (types `ContractPrerequisite_CompletedContractTags`, `ContractResult_CompletionTags`, `Tag`)

---

## Fichiers modifiés

| Fichier | Action |
|---|---|
| `StarXelem/Data/Entities.cs` | Créer `MissionRequiredTagEntity` et `MissionCompletionTagEntity`, ajouter les collections de navigation sur `MissionEntity` |
| `StarXelem/Data/StarXelemDbContext.cs` | Ajouter les deux `DbSet` et configurer les relations Fluent API |
| `StarXelem/Services/LocalDatabaseService.cs` | Parser `additionalPrerequisites` dans `ProcessContractForDb`, parser `CompletionTags` structurellement dans `ProcessMissionRewards` |

---

### Task 1 : Nouvelles entités dans `Entities.cs`

**Files:**
- Modify: `StarXelem/Data/Entities.cs`

- [ ] **Step 1 : Ajouter `MissionRequiredTagEntity` et `MissionCompletionTagEntity` à la fin du fichier**

Ouvrir `StarXelem/Data/Entities.cs` et ajouter après la dernière classe (`BlueprintModifierEntity`) :

```csharp
public class MissionRequiredTagEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    [Required]
    public string TagSelfId { get; set; } = string.Empty;
    [ForeignKey("TagSelfId")]
    public virtual TagEntity? Tag { get; set; }

    /// <summary>true = tag requis (inclusion), false = tag exclu (exclusion)</summary>
    public bool IsRequired { get; set; }

    /// <summary>Nombre de tags requis pour satisfaire le prérequis (requiredCountValue ou excludedCountValue)</summary>
    public int RequiredCount { get; set; }
}

public class MissionCompletionTagEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    [Required]
    public string TagSelfId { get; set; } = string.Empty;
    [ForeignKey("TagSelfId")]
    public virtual TagEntity? Tag { get; set; }
}
```

- [ ] **Step 2 : Ajouter les collections de navigation sur `MissionEntity`**

Dans `Entities.cs`, trouver la classe `MissionEntity` et ajouter deux propriétés de navigation après `BlueprintPools` :

```csharp
public virtual ICollection<MissionRequiredTagEntity> RequiredTags { get; set; } = new List<MissionRequiredTagEntity>();
public virtual ICollection<MissionCompletionTagEntity> CompletionTags { get; set; } = new List<MissionCompletionTagEntity>();
```

- [ ] **Step 3 : Vérifier que le fichier compile**

```powershell
dotnet build "D:\repos\starcitizen\StarXelem\src\StarXelem\StarXelem.csproj" --no-restore
```

Attendu : `Build succeeded` sans erreur.

- [ ] **Step 4 : Commit**

```powershell
git add "src/StarXelem/Data/Entities.cs"
git commit -m "feat: ajouter MissionRequiredTagEntity et MissionCompletionTagEntity"
```

---

### Task 2 : Enregistrement dans le `DbContext`

**Files:**
- Modify: `StarXelem/Data/StarXelemDbContext.cs`

- [ ] **Step 1 : Ajouter les deux `DbSet`**

Dans `StarXelemDbContext.cs`, après la ligne `public DbSet<MissionRewardEntity> MissionRewards`, ajouter :

```csharp
public DbSet<MissionRequiredTagEntity> MissionRequiredTags => Set<MissionRequiredTagEntity>();
public DbSet<MissionCompletionTagEntity> MissionCompletionTags => Set<MissionCompletionTagEntity>();
```

- [ ] **Step 2 : Configurer les relations Fluent API dans `OnModelCreating`**

À la fin de la méthode `OnModelCreating`, avant la fermeture `}`, ajouter :

```csharp
// Mission -> RequiredTags
modelBuilder.Entity<MissionRequiredTagEntity>()
    .HasOne(rt => rt.Mission)
    .WithMany(m => m.RequiredTags)
    .HasForeignKey(rt => rt.MissionId)
    .OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<MissionRequiredTagEntity>()
    .HasOne(rt => rt.Tag)
    .WithMany()
    .HasForeignKey(rt => rt.TagSelfId)
    .OnDelete(DeleteBehavior.Cascade);

// Mission -> CompletionTags
modelBuilder.Entity<MissionCompletionTagEntity>()
    .HasOne(ct => ct.Mission)
    .WithMany(m => m.CompletionTags)
    .HasForeignKey(ct => ct.MissionId)
    .OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<MissionCompletionTagEntity>()
    .HasOne(ct => ct.Tag)
    .WithMany()
    .HasForeignKey(ct => ct.TagSelfId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3 : Vérifier que le fichier compile**

```powershell
dotnet build "D:\repos\starcitizen\StarXelem\src\StarXelem\StarXelem.csproj" --no-restore
```

Attendu : `Build succeeded` sans erreur.

- [ ] **Step 4 : Commit**

```powershell
git add "src/StarXelem/Data/StarXelemDbContext.cs"
git commit -m "feat: enregistrer MissionRequiredTags et MissionCompletionTags dans DbContext"
```

---

### Task 3 : Parser les prérequis de tags dans `LocalDatabaseService`

**Files:**
- Modify: `StarXelem/Services/LocalDatabaseService.cs`

- [ ] **Step 1 : Ajouter l'appel au parsing des prérequis dans `ProcessContractForDb`**

Dans `ProcessContractForDb` (ligne ~535), après l'appel `await ProcessMissionRewards(...)`, ajouter :

```csharp
await ProcessMissionRequiredTagsAsync(mission.Id, contract, db);
```

- [ ] **Step 2 : Implémenter la méthode `ProcessMissionRequiredTagsAsync`**

Ajouter cette méthode dans la région appropriée (près de `ProcessMissionRewards`) :

```csharp
private Task ProcessMissionRequiredTagsAsync(string missionId, ContractBase contract, StarXelemDbContext db)
{
    try
    {
        foreach (var prerequisite in contract.additionalPrerequisites)
        {
            if (prerequisite is not ContractPrerequisite_CompletedContractTags completedTags)
                continue;

            foreach (var tag in completedTags.requiredCompletedContractTags.tags)
            {
                if (tag == null) continue;
                var tagId = tag.selfId.ToString();
                if (string.IsNullOrEmpty(tagId)) continue;

                db.MissionRequiredTags.Add(new MissionRequiredTagEntity
                {
                    MissionId = missionId,
                    TagSelfId = tagId,
                    IsRequired = true,
                    RequiredCount = completedTags.requiredCountValue
                });
            }

            foreach (var tag in completedTags.excludedCompletedContractTags.tags)
            {
                if (tag == null) continue;
                var tagId = tag.selfId.ToString();
                if (string.IsNullOrEmpty(tagId)) continue;

                db.MissionRequiredTags.Add(new MissionRequiredTagEntity
                {
                    MissionId = missionId,
                    TagSelfId = tagId,
                    IsRequired = false,
                    RequiredCount = completedTags.excludedCountValue
                });
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Échec du parsing des prérequis de tags pour la mission {MissionId}", missionId);
    }

    return Task.CompletedTask;
}
```

- [ ] **Step 3 : Vérifier que le fichier compile**

```powershell
dotnet build "D:\repos\starcitizen\StarXelem\src\StarXelem\StarXelem.csproj" --no-restore
```

Attendu : `Build succeeded` sans erreur.

- [ ] **Step 4 : Commit**

```powershell
git add "src/StarXelem/Services/LocalDatabaseService.cs"
git commit -m "feat: parser ContractPrerequisite_CompletedContractTags vers MissionRequiredTags"
```

---

### Task 4 : Parser les tags de complétion dans `ProcessMissionRewards`

**Files:**
- Modify: `StarXelem/Services/LocalDatabaseService.cs`

- [ ] **Step 1 : Enrichir le case `ContractResult_CompletionTags` dans `ProcessMissionRewards`**

Trouver le case (ligne ~1162) et le remplacer par :

```csharp
case ContractResult_CompletionTags completionTags:
    {
        var tagNames = new List<string>();
        foreach (var ct in completionTags.completionTags)
        {
            if (ct?.tag == null) continue;
            tagNames.Add($"'{ct.tag.tagName}'");

            var tagId = ct.tag.selfId.ToString();
            if (!string.IsNullOrEmpty(tagId))
            {
                db.MissionCompletionTags.Add(new MissionCompletionTagEntity
                {
                    MissionId = missionId,
                    TagSelfId = tagId
                });
            }
        }
        db.MissionRewards.Add(new MissionRewardEntity
        {
            MissionId = missionId,
            RewardType = "ContractResult_CompletionTags",
            DisplayValue = $"Tags: {string.Join(", ", tagNames)}",
            IsCalculated = false
        });
    }
    return 1;
```

- [ ] **Step 2 : Vérifier que le fichier compile**

```powershell
dotnet build "D:\repos\starcitizen\StarXelem\src\StarXelem\StarXelem.csproj" --no-restore
```

Attendu : `Build succeeded` sans erreur.

- [ ] **Step 3 : Commit**

```powershell
git add "src/StarXelem/Services/LocalDatabaseService.cs"
git commit -m "feat: parser ContractResult_CompletionTags vers MissionCompletionTags"
```

---

### Task 5 : Reconstruire la BDD et vérifier les données

**Files:**
- Run: `StarXelem.cli.testdb`

- [ ] **Step 1 : Compiler et lancer le projet CLI pour reconstruire la BDD**

```powershell
dotnet run --project "D:\repos\starcitizen\StarXelem\src\StarXelem.cli.testdb\StarXelem.cli.testdb.csproj"
```

Attendu : logs se terminant par `BDD reconstruite avec succes: C:\Users\...\AppData\Local\StarXelem\database.db`

- [ ] **Step 2 : Vérifier les données dans la BDD SQLite**

```powershell
& "sqlite3" "C:\Users\$env:USERNAME\AppData\Local\StarXelem\database.db" "SELECT COUNT(*) FROM MissionRequiredTags;"
& "sqlite3" "C:\Users\$env:USERNAME\AppData\Local\StarXelem\database.db" "SELECT COUNT(*) FROM MissionCompletionTags;"
```

Attendu : au moins une valeur > 0 dans chaque table (vérifier que les données ont bien été insérées).

- [ ] **Step 3 : Vérifier un exemple concret — mission InterSec Patrol**

```powershell
& "sqlite3" "C:\Users\$env:USERNAME\AppData\Local\StarXelem\database.db" @"
SELECT m.DebugName, t.Name, rt.IsRequired, rt.RequiredCount
FROM MissionRequiredTags rt
JOIN Missions m ON m.Id = rt.MissionId
JOIN Tags t ON t.SelfId = rt.TagSelfId
WHERE m.DebugName LIKE '%InterSec%'
LIMIT 20;
"@
```

Attendu : lignes listant les tags requis/exclus pour les missions InterSec Patrol.

- [ ] **Step 4 : Commit final**

```powershell
git add -A
git commit -m "feat: vérification BDD - prérequis et complétion de tags intégrés"
```
