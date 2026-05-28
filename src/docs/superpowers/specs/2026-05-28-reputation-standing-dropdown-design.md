# Reputation — Accordéon Standing par Scope

**Date** : 2026-05-28
**Branche** : `feature/ajout_reputation`
**Contexte** : [#17.7 CLAUDE_design_convention.md](CLAUDE_design_convention.md#177-liste-des-ranks-scope-d%C3%A9pli%C3%A9)

## Problème

La vue Reputation affiche chaque scope de réputation avec une barre de progression, mais l'utilisateur ne voit pas les autres standings disponibles pour ce scope ni où il se situe par rapport à eux.

## Solution

Ajouter un accordéon cliquable par scope. Développer un scope révèle la liste de tous ses standings (ranks) avec le standing actuel surligné. Interaction purement locale : développer un scope n'affecte pas les autres. La sélection dans le dropdown est en affichage uniquement — aucune action gRPC n'est envoyée.

## Design UI

### Comportement

- **Clic sur le label du scope** (ou chevron adjacent) → développe/replie la liste des standings.
- **Chevron `›`** à gauche du label : pivote de 90° vers `▼` au développement. Transition `0.15s`.
- **Plusieurs scopes** peuvent être déployés simultanément.
- **État par défaut** : replié.

### Structure de la ligne de scope (modifiée)

```
[ chevron › ] [ scope label 10px/500/caps ] [ valeur brute monospace 10px ]
```

- Valeur brute : `#47FFFFFF` (dark) / `#FF888888` (light), monospace 10px — toujours visible (§17.8)
- Label scope : `#33FFFFFF` (dark) / `#FFBBBBBB` (light), 10px/500/caps (§17.4)

### Structure de la liste dépliée

Grille à 3 colonnes : `[ point 7×7px ] [ nom (flex) ] [ Min (monospace) ]`

- Padding : `4px 6px`
- Border-radius : `5px`
- Police : `10px / 500`
- Gap : `8px`
- Séparateur haut de liste : `#0FFFFFFF` (dark) / `#FFECECEC` (light) — `0.5px` (§17.7)

#### Rang courant (surligné)

| Élément | Dark | Light |
|---|---|---|
| Fond de ligne | `#1A1D9E75` | `#FFE1F5EE` |
| Nom | `#FF5DCAA5` | `#FF0F6E56` |
| Seuil | `#80FFFFFF` | `#FF444444` |

#### Ranks non atteints

- `opacity: 0.45` (§17.7)
- Nom : `#80FFFFFF` (dark) / `#FF666666` (light) — rank verrouillé (§17.7)
- Seuil : `#47FFFFFF` (dark) / `#FF999999` (light)

#### Point de palier

- Pastille `7×7px` avec la couleur du palier (§17.4)
- P7 Master : outline `1.5px`, outline-offset `1px` (§17.7)

### Sélection du rang courant

```
rangCourant = dernier rank tel que rank.Min <= scope.CurrentValue
```

Le calcul est fait côté ViewModel, pas en dur côté vue.

## Modifications

### Fichiers touchés

| Fichier | Type de modification |
|---|---|
| `Views/ReputationTabView.axaml` | Ajout accordéon, liste de standings, styles |
| `Models/ReputationModel.cs` | Ajout propriété `IsExpanded` |
| `ViewModels/ReputationTabViewModel.cs` | Sans modification |

### Détails

#### `ReputationModel.cs`

Ajouter une propriété booléenne pour l'état de l'accordéon :

```csharp
public bool IsExpanded { get; set; }
```

#### `ReputationTabView.axaml`

- Remplacer le `DockPanel` du label de scope par un panneau cliquable avec chevron pivotant.
- Ajouter sous la barre de progression une zone conditionnelle (`DataTrigger` ou `Selector`) qui affiche la liste des standings quand `IsExpanded == true`.
- La liste est un `ItemsControl` bindé sur `StandingList` avec un `DataTemplate` montrant le point, le nom et la valeur Min.
- Le standing courant est identifié par comparaison avec `CurrentStanding.Name`.
- Appliquer les couleurs du design system (§17.7).

## Contraintes

- Respecter le design system §17 de CLAUDE_design_convention.md — couleurs, tailles, espacements exacts.
- Pas de modification de ViewModel — l'état de l'accordéon est porté par le modèle.
- Pas de commande gRPC — affichage uniquement.
- Bordures à `0.5px` sauf cas justifié (§15).
