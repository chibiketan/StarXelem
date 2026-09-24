# Sonde gRPC `EntityQueryAsync` (CLI de test)

Le projet `src/StarXelem.cli.testdb` contient une sonde qui appelle directement la méthode gRPC
`EntityGraphService.EntityQueryAsync`, celle qu'utilise `GrpcClientService.QueryGraphBySearch` pour charger les
objets du joueur. Chaque paramètre de la requête se règle en ligne de commande, ce qui permet de comparer une
requête acceptée à une requête rejetée (« paramètres incorrects ») et d'identifier le paramètre en cause.

Code : `src/StarXelem.cli.testdb/EntityQueryProbe.cs`. Le dispatch est fait dans `Program.cs`, avant l'ouverture du P4K.

## Sommaire

1. [Prérequis](#prérequis)
2. [Lancer la sonde](#lancer-la-sonde)
3. [Options](#options)
4. [Lire les résultats](#lire-les-résultats)
5. [Requête libre en JSON](#requête-libre-en-json)
6. [Recettes](#recettes)
7. [Pièges et limites](#pièges-et-limites)

## Prérequis

- Le jeu (ou le launcher) doit être **lancé et connecté** : la sonde lit le fichier `loginData.json`, comme
  `GrpcClientService.InitClient`. Elle le cherche dans tous les sous-dossiers du dossier qui contient `Data.p4k`.
  Si le fichier n'existe pas, elle attend qu'il soit écrit pendant 30 secondes, puis abandonne.
- Le P4K n'est **pas ouvert** : seul son chemin sert à localiser `loginData.json`. Le lancement est donc rapide.
- Aucune requête n'est envoyée avec `--print-only` : le jeu n'a alors pas besoin d'être lancé.

Les jetons d'authentification (jeton de login et JWT) ne sont jamais affichés.

## Lancer la sonde

Depuis la racine du dépôt :

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc [chemin\Data.p4k] [options]
```

Points d'attention :

- **Le `--` avant `--probe-grpc` est nécessaire** : il sépare les arguments de `dotnet run` de ceux du programme.
- **Les options s'écrivent `--cle=valeur`, jamais `--cle valeur`.** `Program.cs` considère comme chemin du P4K tout
  argument qui ne commence pas par `--` : avec `--owner 123`, l'option est ignorée et la valeur `123` est prise pour
  le chemin du P4K. La sonde s'arrête alors avec « Fichier P4K introuvable : 123 » et le code de sortie `1`.
- **Chemin du P4K (facultatif).** Sans chemin, la première installation détectée est utilisée ; le journal l'indique
  (`Using P4K: ...`). Si plusieurs canaux sont installés (LIVE, PTU, HOTFIX), passer le chemin explicitement pour
  cibler le bon : le `loginData.json` recherché est celui de ce dossier.
- Un argument qui contient des espaces se met entre guillemets, en quoi englobant l'option entière :
  `"--json=C:\mes tests\requete.json"`.
- Après une première compilation, `--no-build` évite de recompiler à chaque lancement.

Aide intégrée :

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --help
```

Exemple minimal (les objets du joueur connecté, tri par `geid`, première page de 250) :

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --owner=me
```

## Options

Sans option de filtre, la requête est envoyée avec un filtre `ET` vide, comme le fait l'application dans ce cas.

### Filtres

Les filtres sont combinés par un **ET**, comme dans `QueryGraphBySearch`.

| Option | Effet dans la requête |
|---|---|
| `--owner=<geid\|me>` | Filtre de propriété `ownerId` égal au geid donné. `me` désigne le joueur connecté. |
| `--geid=<geid>` | Filtre de propriété `geid` égal à la valeur : un objet précis. |
| `--types=<a,b,...>` | Filtre de propriété `itemTypeEnum` avec l'opérateur `IN`. Accepte des noms d'`EItemType` (insensible à la casse, par exemple `Drink`) ou des entiers, mélangés si besoin. |
| `--stowed-in=<id,id,...>` | Filtre d'arête `STOWED_IN` vers ces conteneurs, dans un groupe `OU`. |
| `--or-owner` | Avec `--stowed-in` : ajoute au groupe `OU` la condition `ownerId` égal au joueur connecté. Sans effet sans `--stowed-in`. |
| `--parent-urn=<a,b,...>` | Filtre de propriété `parentUrn` avec l'opérateur `IN`. |

### Requête

| Option | Valeur par défaut | Effet |
|---|---|---|
| `--inventory-id=<id>` | vide | `Query.InventoryId`. |
| `--shard=<id>` | vide | `Scope.ShardId` (le type de portée est toujours `SCOPE_TYPE_GLOBAL`). |
| `--language=<code>` | vide | `Query.Language`. |
| `--first=<n>` | `250` | Taille de page (`Pagination.First`). |
| `--after=<curseur>` | vide | Curseur de départ (`Pagination.After`). |
| `--sort-property=<nom>` | `geid` | Propriété de tri. Le comparateur est toujours numérique et l'ordre croissant. |
| `--no-sort` | tri envoyé | N'envoie aucun bloc de tri. |

### Projection

Les valeurs par défaut sont celles de `QueryGraphBySearch`.

| Option | Effet |
|---|---|
| `--tree` | Active la projection en arbre (`Tree.Enabled`). Désactivée par défaut ; l'application l'active avec la case « Charger les objets liés ». |
| `--tree-inventory-nodes` | `Tree.IncludeInventoryNodes = true` (faux par défaut). |
| `--no-path-mode` | `Tree.PathMode = false` (vrai par défaut). |
| `--no-outgoing-edges` | `OutgoingEdges = false` (vrai par défaut). |
| `--no-snapshots` | `Snapshots = false` (vrai par défaut). |
| `--entity-classes` | `EntityClasses = true` (faux par défaut). |

`SnapshotFlags` et `SnapshotExcludeFlags` sont toujours à `0`. Pour les modifier, passer par une [requête JSON](#requête-libre-en-json).

### Exécution et affichage

| Option | Effet |
|---|---|
| `--json=<fichier>` | Envoie une requête (ou un tableau de requêtes) écrite en JSON. **Les options de filtre, de requête et de projection sont alors ignorées.** Voir [Requête libre en JSON](#requête-libre-en-json). |
| `--all-pages` | Suit la pagination jusqu'à la dernière page (`HasNextPage`). Sans elle, seule la première page est demandée. |
| `--dump-response` | Affiche chaque réponse complète en JSON, avant le résumé. |
| `--max-rows=<n>` | Nombre de nœuds listés dans le résumé (`20` par défaut). |
| `--print-only` | Affiche la requête sans se connecter ni l'envoyer. Le geid du joueur connecté est remplacé par `0`. |
| `--help` | Affiche l'aide et quitte. |

## Lire les résultats

Pour chaque requête, la sonde affiche d'abord la requête au format JSON, prête à être copiée dans un fichier pour
`--json=`. Les champs qui valent leur valeur par défaut (chaîne vide, `0`, `false`) n'apparaissent pas dans ce JSON :
en proto3, ils ne sont pas non plus transmis sur le fil, à l'exception des champs déclarés `optional`.

### Requête acceptée

```
-> ACCEPTÉE en <n> ms : <p> page(s), <n> nœud(s), <n> arête(s), <n> snapshot(s)
   dernière page : hasNextPage=<True|False> endCursor="<curseur>"
   geid=<geid> crc=<crc32 de la classe> type=<EItemType>
   ...
   répartition par type : <type>=<nombre>, ...
```

- `crc` est la valeur que l'application utilise pour retrouver le nom d'un objet dans la table `ScItems`.
- `dernière page` sert à contrôler la pagination : avec `hasNextPage=True` sans `--all-pages`, il reste des données.

### Requête rejetée

```
-> REJETÉE après <n> ms
   code   : <StatusCode gRPC, par exemple InvalidArgument>
   détail : <message du serveur>
   trailer <clé> : <valeur>
   trailer grpc-status-details-bin (<n> octets) : <chaînes lisibles> | <chaînes lisibles>
```

Le trailer `grpc-status-details-bin` contient un `google.rpc.Status` sérialisé, où le serveur décrit souvent le
champ ou la valeur en cause. Sans le type protobuf correspondant, la sonde en extrait les chaînes lisibles (au moins
4 caractères), ce qui suffit en général à repérer le champ fautif.

### Plusieurs requêtes et code de sortie

Avec un tableau JSON, un bilan final indique combien de requêtes ont été acceptées ou rejetées. Code de sortie du processus :

| Code | Signification |
|---|---|
| `0` | Toutes les requêtes ont été acceptées (ou `--print-only` / `--help`). |
| `1` | Au moins une requête rejetée, ou connexion impossible (aucune donnée de login, échec d'identification). |
| `2` | Option invalide, fichier JSON introuvable ou JSON rejeté à l'analyse. |

## Requête libre en JSON

`--json=<fichier>` permet d'écrire la requête entière à la main, y compris des combinaisons que les options ne couvrent pas.
Le fichier contient soit **un objet** `EntityQueryRequest`, soit **un tableau** d'objets ; les requêtes d'un tableau
sont envoyées l'une après l'autre.

Le plus simple pour démarrer est de lancer `--print-only` avec des options proches de ce qu'on veut, puis de copier le JSON affiché.
Exemple avec `--owner=me --inventory-id=123` :

```json
{
  "body": {
    "scope": { "type": "SCOPE_TYPE_GLOBAL" },
    "query": {
      "filter": {
        "compositeFilter": {
          "operator": "LOGICAL_OPERATOR_AND",
          "filters": [
            {
              "propertyFilter": {
                "property": "ownerId",
                "operator": "COMPARISON_OPERATOR_EQUAL",
                "values": [ { "unsignedBigintValue": "{{me}}" } ]
              }
            }
          ]
        }
      },
      "pagination": { "first": 250 },
      "projection": {
        "tree": { "pathMode": true },
        "snapshots": true,
        "outgoingEdges": true
      },
      "sort": {
        "order": "PAGINATION_ORDER_ASCENDING",
        "entityProperty": { "property": "geid", "sortComparator": "SORT_COMPARATOR_NUMERICAL" }
      },
      "inventoryId": "123"
    }
  }
}
```

- **`{{me}}`** est remplacé par le geid du joueur connecté avant l'analyse du fichier (par `0` avec `--print-only`).
  Le fichier reste ainsi valable d'un compte à l'autre. Comme le JSON protobuf écrit les entiers 64 bits sous
  forme de chaînes, le remplaçant se place entre guillemets : `"{{me}}"`.
- **L'analyse est stricte** : un nom de champ inconnu (par exemple `"paginaton"`) est rejeté côté client avec
  `JSON de requête invalide : Unknown field: ...`, au lieu d'être ignoré. Dans ce cas la requête n'est pas envoyée.
- Les noms sont en `camelCase` et les valeurs d'énumération en toutes lettres (`LOGICAL_OPERATOR_AND`, ...). Pour
  connaître les noms exacts d'une énumération, produire d'abord la requête avec les options et `--print-only`.
- Pour comparer plusieurs variantes en un lancement, mettre chaque variante dans un tableau : le bilan final donne le
  nombre d'acceptées et de rejetées, et le détail de chaque rejet est affiché sous sa requête.

## Recettes

**Voir la requête d'une combinaison d'options sans rien envoyer**

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --print-only --owner=me --types=Drink,12 --first=50
```

**Reproduire l'appel de l'application pour un conteneur**

Quand des inventaires sont sélectionnés, l'application envoie une requête par inventaire : `InventoryId` reçoit
l'identifiant de l'inventaire, et le filtre `STOWED_IN` liste tous les inventaires choisis, avec un `OU` sur le
propriétaire quand « Utiliser le compte connecté » est coché (c'est le cas par défaut).

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --inventory-id=<id> --stowed-in=<id1>,<id2> --or-owner
```

**Tester l'effet de `InventoryId`** : lancer la même requête avec puis sans `--inventory-id=`. Un commentaire de
`QueryGraphBySearch` indique qu'un champ vide provoque une erreur : la sonde permet de le vérifier et de lire le message.

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --stowed-in=<id>
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --stowed-in=<id> --inventory-id=<id>
```

**Retrouver un objet précis et voir sa réponse complète**

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --geid=<geid> --dump-response
```

**Isoler l'effet d'un paramètre de la projection ou du tri**

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --owner=me --no-sort
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --owner=me --no-snapshots --no-outgoing-edges
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --owner=me --tree
```

**Parcourir toutes les pages et compter les objets par type**

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc --stowed-in=<id> --inventory-id=<id> --all-pages --max-rows=0
```

**Rejouer une série de variantes depuis un fichier**

```powershell
dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc "--json=C:\chemin\variantes.json"
```

## Pièges et limites

- **Syntaxe `--cle=valeur` obligatoire** (voir [Lancer la sonde](#lancer-la-sonde)). Une option écrite sans `=` est
  ignorée, et sa valeur est prise pour le chemin du P4K : seul un message « Fichier P4K introuvable » signale l'erreur.
  Une option inconnue écrite avec `=` (par exemple `--ownr=me`) est en revanche **ignorée sans aucun message** : en cas
  de résultat inattendu, vérifier l'orthographe des options dans la requête JSON affichée.
- **Un même lancement n'utilise qu'un compte et qu'un canal** : ceux du `loginData.json` trouvé. Pour tester un autre
  canal, passer le chemin de son `Data.p4k`.
- **Les en-têtes sont ceux de l'application** : `Authorization: Bearer <JWT du joueur>` et `grpc-timeout: 60S`. La
  sonde ajoute en plus une échéance de 90 secondes par appel.
- **`--all-pages` fait avancer le curseur dans la requête envoyée** : chaque page reprend le `endCursor` de la
  précédente. Pour repartir d'un curseur précis, utiliser `--after=`.
- **`--or-owner` n'a de sens qu'avec `--stowed-in`.** Pour un filtre `ownerId` seul, utiliser `--owner=me`.
- **`--print-only` ne montre pas le vrai geid** : il est remplacé par `0`.
- **La sonde est en lecture seule** : `EntityQueryAsync` interroge le graphe d'entités, rien n'est modifié.
- **Aucune requête n'a encore été validée contre le serveur** au moment de la rédaction de ce document : les exemples de
  sortie ci-dessus décrivent la forme des messages, les valeurs entre chevrons sont à remplacer.

## Fichiers concernés

| Fichier | Rôle |
|---|---|
| `src/StarXelem.cli.testdb/EntityQueryProbe.cs` | La sonde : connexion, construction de la requête, envoi, affichage. |
| `src/StarXelem.cli.testdb/Program.cs` | Aiguille vers la sonde quand `--probe-grpc` est présent, avant l'ouverture du P4K. |
| `src/StarXelem.cli.testdb/StarXelem.cli.testdb.csproj` | Références vers les assemblies gRPC (`Grpc.Net.Client`, `Grpc.Core.Api`, `Google.Protobuf`, `StarBreaker.Grpc`). |
| `src/StarXelem/Services/GrpcClientService.cs` | `QueryGraphBySearch` : la méthode dont la sonde reproduit la requête. |
| `src/StarXelem/Services/StarCitizenClientWatcher.cs` | Lecture de `loginData.json`. |
