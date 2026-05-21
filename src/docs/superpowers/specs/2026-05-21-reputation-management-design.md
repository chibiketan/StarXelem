# Design: Gestion des Réputations

Date: 2026-05-21
Status: Approved

## 1. Objectif
Ajouter une nouvelle page permettant au joueur de consulter sa progression de réputation auprès des différents contractants du jeu. L'écran doit fusionner des données statiques (fichiers P4K) et des données dynamiques (gRPC).

## 2. Architecture Technique

### 2.1 Couche Service
- **`IGrpcClientService`** : Ajout de la méthode `Task<List<VersionedReputation>> QueryReputationsAsync()`.
- **`IReputationService` (Nouveau)** : Service de domaine responsable de la fusion des données.
    - Méthode `Task<List<ContractorModel>> GetSynchronizedReputationsAsync()`.
    - Logique : 
        1. Récupération de la liste complète des réputations via `IP4kService`.
        2. Récupération des réputations actives du joueur via `IGrpcClientService`.
        3. Fusion des scores gRPC sur la liste exhaustive P4K.
        4. Groupement par Contractant.
        5. Tri alphabétique par nom de contractant.

### 2.2 Modèles de Données (`Models/`)
- **`ReputationModel`** :
    - `string Category` (ex: Security)
    - `string TierName` (Nom du palier atteint)
    - `float CurrentValue` (Valeur actuelle)
    - `float MaxValue` (Valeur max du palier/total)
- **`ContractorModel`** :
    - `string Name` (Nom du contractant)
    - `List<ReputationModel> Reputations`

### 2.3 Couche ViewModel (`ViewModels/`)
- **`ReputationTabViewModel`** :
    - `ObservableCollection<ContractorViewModel>` : Liste filtrée affichée.
    - `string SearchText` : Texte de recherche.
    - `IAsyncRelayCommand LoadDataCommand` : Déclenche la synchronisation via `IReputationService`.

## 3. Design de l'Interface (UI)

### 3.1 Mise en page
- **Header** : 
    - Champ de recherche (conforme au Design System section 9).
    - Bouton "Charger les données" (conforme au Design System section 8).
- **Contenu** : `ScrollViewer` $\rightarrow$ `WrapPanel` $\rightarrow$ `ContractorCard`.

### 3.2 La Fiche Contractant (`ContractorCard`)
- **Titre** : Nom du contractant (13px, 500).
- **Liste de Réputations** :
    - Pour chaque réputation :
        - Label : `Category` + `TierName`.
        - Indicateur : `ProgressBar` violette (AccentBrush) remplie selon `CurrentValue / MaxValue`.
- **Style** : Bordures `0.5px`, radius `8-10px`, fond "Carte / bloc de contenu".

## 4. Flux de Données
1. L'utilisateur ouvre la page $\rightarrow$ `OnFirstShowAsync()` charge les données initiales.
2. Saisie dans `SearchText` $\rightarrow$ Filtrage local immédiat de la liste des contractants.
3. Clic sur "Charger les données" $\rightarrow$ Appel gRPC $\rightarrow$ Mise à jour des scores $\rightarrow$ Rafraîchissement de l'UI.

## 5. Critères de Succès
- Affichage correct de toutes les réputations même avec 0 point.
- Recherche fluide sur le nom du contractant.
- Respect strict du Design System StarXelem.
- Découplage total entre les objets gRPC et les modèles de vue.
