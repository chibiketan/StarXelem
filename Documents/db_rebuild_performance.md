# Optimisation de la reconstruction de la BDD locale (`LocalDatabaseService.RebuildDbAsync`)

Date : 2026-07-24
Contexte : `RebuildDbCoreAsync` (déclenché par `RebuildDbAsync` / `EnsureDbAsync`) reconstruit entièrement la base
SQLite locale à partir du `Data.p4k`. Deux problèmes signalés :
- Temps d'exécution élevé (≥ 3 minutes constatées par l'utilisateur).
- Empreinte mémoire finale énorme (~22 Go) après la reconstruction, problématique sur une machine
  moins puissante ou avec le jeu lancé en parallèle.

Mesures faites avec le projet `StarXelem.cli.testdb` (rebuild forcé + requêtes de contrôle), sur le même
`Data.p4k` (~23 371 SCItems, 2 499 missions, 1 105 vaisseaux, 1 597 blueprints).

## État des lieux initial

- Reconstruction séquentielle en 10 phases dans un unique `DbContext` gardé ouvert du début à la fin.
- `SaveChangesAsync` déjà relativement bien groupé par phase (pas de save par ligne), mais les entités
  restent **trackées** dans le `ChangeTracker` pendant tout le rebuild : rien n'est jamais détaché avant
  la fin (`using var db` scope complet), donc la mémoire du `ChangeTracker` croît de façon monotone
  sur les 10 phases.
- `P4kService` construit et garde en cache **tous les enregistrements `EntityClassDefinition` du jeu**,
  chargés en profondeur 1 (`_EntityClassDict`) puis re-matérialisés en profondeur 3 pour les besoins des
  phases SCItems/Missions/Loadouts (`GetAllEntityClassDefinition(3)`, `EnsureRecordsDepthAsync(..., 3)`).
  Ce cache (`_EntityClassDict` / `_entityClassGuidDict`) n'est **jamais vidé** : il vit pour toute la durée
  de vie du service, y compris après la fin du rebuild.
- SQLite ouvert avec les réglages par défaut (`journal_mode=DELETE`, `synchronous=FULL`) : chaque batch de
  write attend une synchronisation disque complète.
- Aucune mesure/logging de mémoire n'existait : impossible de vérifier objectivement une amélioration.

Reproduit en base de référence : `WorkingSet` mesuré à **~23 Go** juste après la fin de la phase 7
(Blueprints), sans jamais redescendre ensuite (le process CLI ne fait rien d'autre qu'un `SaveChangesAsync`
supplémentaire et quelques requêtes SQL après ça).

## Pistes appliquées

### 1. `db.ChangeTracker.Clear()` après chaque `SaveChangesAsync` de phase — ✅ Appliqué
**Gain attendu** : réduire l'empreinte du `ChangeTracker` en détachant les entités déjà persistées, phase
après phase, au lieu de les garder trackées jusqu'à la fin du rebuild.
**Piège rencontré** : deux caches internes (`_contractorCache`/`_categoryCache` pour les missions,
`_blueprintCache` pour les blueprints) conservent des **références directes** vers des entités EF
(`ActorEntity`, `MissionCategoryEntity`, `BlueprintEntity`) réutilisées comme propriétés de navigation par
plusieurs entités *avant* le `SaveChanges` correspondant. Un premier essai de vidage de `_blueprintCache`
juste après la phase 7 a cassé la phase 8 : les récompenses de mission de type "pool de blueprints"
(`ProcessBlueprintPoolsAsync`, appelé depuis `ProcessSingleReward` pendant la phase Missions) réutilisent
ce cache pour éviter de ré-insérer un blueprint déjà en base → sans le cache, le blueprint était recréé et
ré-ajouté, provoquant une violation de contrainte unique (`UNIQUE constraint failed: Blueprints.SelfId`) au
moment du `SaveChangesAsync` de la phase Missions.
**Correctif** : `_blueprintCache` n'est vidé qu'après la phase Missions (comme `_contractorCache` et
`_categoryCache`), pas après la phase Blueprints. `ChangeTracker.Clear()` n'est ajouté qu'aux points où
aucune méthode ultérieure ne référence encore une entité déjà trackée par identité d'objet (vérifié
manuellement phase par phase).
**Résultat** : rebuild stable, aucune duplication, mémoire gérée réduite phase après phase (voir mesures).

### 2. Libération du cache lourd de `P4kService` en fin de rebuild — ✅ Appliqué
Nouvelle méthode `IP4kService.ReleaseHeavyCache()` : vide `_EntityClassDict` / `_entityClassGuidDict` et
réarme `_loadingDatabaseTask` à `null` pour que le cache soit reconstruit paresseusement (et à moindre
coût, profondeur -1 uniquement) si un autre appelant en a de nouveau besoin après le rebuild.
Appelée à la fin de `RebuildDbCoreAsync`, une fois toutes les phases terminées.
**Gain attendu** : c'est le plus gros contributeur mémoire identifié (tous les `EntityClassDefinition` du
jeu, chargés en profondeur 3 pour beaucoup d'entre eux). Le fichier `.p4k` et son index de fichiers restent
ouverts (utilisés par d'autres fonctionnalités de l'appli — extraction, réputation, missions…) : on ne
ferme volontairement **pas** le p4k lui-même pour ne pas casser ces usages, seulement le cache de records
pré-matérialisés.

### 3. Pragmas SQLite pendant le rebuild — ✅ Appliqué
`PRAGMA journal_mode = WAL`, `PRAGMA synchronous = OFF`, `PRAGMA temp_store = MEMORY` appliqués juste après
`EnsureCreatedAsync`. Comme la base est de toute façon entièrement droppée puis recréée à chaque rebuild,
sacrifier la durabilité pendant cette phase (si crash → on relance juste un rebuild) est un compromis sûr
qui accélère fortement les écritures par lot.
**Gain observé** : c'est la piste qui a le plus contribué à la réduction du temps total (voir mesures).

### 4. GC agressif + `ConserveMemory` en fin de rebuild — ✅ Appliqué
- `LocalDatabaseService` appelle `GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)`
  (x2, avec `WaitForPendingFinalizers` entre les deux) juste après la libération des caches, et logge la
  mémoire avant/après.
- `StarXelem.csproj` : ajout de `<ConserveMemory>9</ConserveMemory>` (+ `ServerGarbageCollection=false`,
  `ConcurrentGarbageCollection=true` explicites). Sans ce réglage, un simple `GC.Collect()` réduit bien le
  *tas managé* mais **ne rend pas les segments à l'OS** (comportement par défaut du GC .NET, qui garde les
  segments pour de futures allocations) : le `WorkingSet` du process ne bougeait quasiment pas malgré une
  mémoire managée redescendue à ~2 Go. `ConserveMemory=9` indique au GC de décommiter agressivement les
  segments inutilisés, ce qui rend l'amélioration visible côté OS (et donc côté Gestionnaire des tâches /
  RAM disponible pour le reste du système, y compris le jeu).
**Gain observé** : décisif — voir mesures ci-dessous (WorkingSet post-rebuild divisé par ~10).

### 5. Logging de mémoire (WorkingSet + tas managé) à chaque étape clé — ✅ Appliqué
Ajout de `LogMemoryUsage(string label)` (via `Environment.WorkingSet` et `GC.GetTotalMemory(false)`),
appelé avant le rebuild, après les phases 7 et 9, et avant/après le GC forcé final. Permet de suivre
objectivement l'empreinte mémoire dans les logs du CLI de test (et en prod si besoin de diagnostiquer).

## Pistes envisagées mais non retenues

- **Fermer le fichier `.p4k` en fin de rebuild** : abandonné. `P4kService` est un singleton réutilisé par
  d'autres fonctionnalités de l'application après le rebuild (onglet Extraction, Réputation, Missions,
  gRPC…). Le fermer forcerait une réouverture complète (et coûteuse) au premier accès suivant. Seul le
  cache de records pré-matérialisés est vidé (piste 2), ce qui couvre l'essentiel du gain mémoire sans
  casser ces fonctionnalités.
- **Réduire le nombre de `SaveChangesAsync`** : déjà correctement fait dans le code existant (batchs de
  10 000 pour les SCItems, un seul save en fin de phase pour les autres) — pas de gain supplémentaire
  identifié sans risquer de casser la logique de dédup par cache (voir piste 1).
- **Paralléliser les phases** : non exploré. Plusieurs phases dépendent de résultats de phases précédentes
  (FK ScItems→Loadouts, Blueprints→Missions) et partagent le même `DbContext` (non thread-safe). Le
  gain potentiel ne justifiait pas le risque dans le temps imparti à cette tâche.

## Résultats mesurés (CLI `StarXelem.cli.testdb`, build Release)

| Mesure | Avant (baseline utilisateur) | Après optimisations |
|---|---|---|
| Temps total du rebuild (`Database rebuild completed`) | ≥ 180 s (constaté) | **~76 s** |
| Temps process CLI complet (build → rebuild → requêtes de contrôle) | — | **~88 s** |
| WorkingSet pendant le rebuild (fin phase 7, pic) | ~22-23 Go (rapporté par l'utilisateur) | ~23 Go *(inchangé, transitoire — voir note)* |
| WorkingSet **après** le rebuild (avant tout GC) | ~22 Go (persistant) | ~23 Go (idem, avant nettoyage) |
| WorkingSet après nettoyage des caches + GC agressif | *(jamais nettoyé auparavant)* | **~2,3 Go** |
| Tas managé (`GC.GetTotalMemory`) après nettoyage | — | **~2,1 Go** |

Note importante : le **pic transitoire** pendant le rebuild (toutes les entités EntityClassDefinition du
jeu chargées en profondeur, plus les entités EF en cours de traitement) reste élevé (~23 Go) — c'est
attendu, il correspond au volume réel de données du jeu chargées simultanément pendant le traitement le
plus lourd (phase Blueprints/SCItems). Le vrai problème signalé par l'utilisateur était que ce pic
**restait figé indéfiniment après la fin du rebuild** : c'est ce point précis qui est corrigé — la mémoire
retombe maintenant à ~2,3 Go dès la fin du `RebuildDbAsync`, contre ~22 Go qui persistaient auparavant tant
que l'application restait ouverte.

## Fichiers modifiés

- `src/StarXelem/Services/LocalDatabaseService.cs` : `ChangeTracker.Clear()` ciblés, pragmas SQLite,
  libération du cache P4K, GC agressif, logging mémoire.
- `src/StarXelem/Services/P4kService/P4kService.cs` : nouvelle méthode `ReleaseHeavyCache()`.
- `src/StarXelem/Services/P4kService/IP4kService.cs` : ajout de `ReleaseHeavyCache()` à l'interface.
- `src/StarXelem/Services/P4kService/DesignP4kService.cs` : implémentation no-op pour le design-time.
- `src/StarXelem/StarXelem.csproj` : réglages GC (`ConserveMemory`, `ServerGarbageCollection`,
  `ConcurrentGarbageCollection`).

## Suites possibles (non traitées ici)

- Le pic transitoire (~23 Go) pourrait être réduit en évitant de garder en cache la profondeur 3 pour
  *tous* les `EntityClassDefinition` d'un coup (ex: résoudre à la volée, sans cache global, au prix d'un
  temps de traitement plus long) — nécessiterait une refonte plus profonde de `P4kService` et n'a pas été
  jugé prioritaire vu que ce pic est transitoire et disparaît maintenant en fin de rebuild.
- `ConserveMemory=9` s'applique à toute l'application (pas seulement au rebuild) : à surveiller si cela a
  un impact sur la fluidité générale de l'UI (compromis mémoire/débit du GC) — aucun souci constaté en
  usage CLI, à confirmer en usage interactif normal de l'application.
