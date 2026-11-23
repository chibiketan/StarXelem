## [0.3.0]
Version SC : 4.10.3

### Ajouté
- Ajout d'une fiche loadout pour chaque vaisseau possédé, accessible depuis Mon hangar : composants, armes, équipement de minage et comparaison avec le loadout d'origine (filtre « Écarts seulement »)
- Affichage du loadout sauvegardé à l'ASOP pour les vaisseaux qui ne sont pas dans l'univers (détruits ou pas encore récupérés à ce patch), complété par le loadout d'origine pour les emplacements non sauvegardés
- Signalement des objets craftés dans la fiche loadout, avec leurs valeurs améliorées et les matériaux utilisés en infobulle
- Ajout des options CLI `--snapshots=<urn>` et `--resolve-guids=<guids>` à la sonde gRPC pour analyser les loadouts sauvegardés

### Corrigé
- Correction de l'affichage du nom personnalisé des vaisseaux, désormais lu dans la configuration de l'entité

### Modifié
- Migration de l'interface vers Avalonia 12 (FluentAvaloniaUI 3, FluentIcons 2.1, AsyncImageLoader 4)
- Mise à jour des bibliothèques .NET (Entity Framework Core, Microsoft.Extensions) en 10.0.15 et de ClosedXML en 0.105.1

### Sécurité
- Correction de vulnérabilités dans des dépendances (SQLite embarqué via SQLitePCLRaw, Tmds.DBus.Protocol)

## [0.2.9]
Version SC : 4.10.3

### Ajouté
- Ajout de l'option `--grpc-trace <dossier>` pour enregistrer les requêtes et réponses gRPC afin de les analyser

### Corrigé
- Correction de la récupération des objets sans propriétaire (ressources minées, objets récupérés) rangés dans un conteneur ou un inventaire du joueur

## [0.2.8]
Version SC : 4.10.3

### Modifié
- Refonte de l'interface graphique

## [0.2.7]
Version SC : 4.10.3

### Modifié
- Mise à jour de Starbreaker pour utiliser le contenu des datafiles de la 4.10.3

## [0.2.6]
Version SC : 4.10.2

### Modifié
- Mise à jour de Starbreaker pour utiliser le contenu des datafiles de la 4.10.2

## [0.2.5]
Version SC : 4.10.1

### Ajouté
- Ajout d'un écran pour voir les armes FPS
- Ajout des véhicules basés sur un acteur (ARGO ATLS et ses variants, PowerSuit) à la base locale des vaisseaux

### Corrigé
- Amélioration de la réactivité de l'écran des objets : le filtrage ne fige plus l'interface
- Ajout d'un indicateur de chargement et de filtrage sur l'écran des objets
- Correction de la récupération des objets lorsque plusieurs conteneurs sont interrogés, qui en omettait une partie
- Correction de l'affichage du nom du vaisseau pour l'écran des objets
- Correction de la résolution du contenant de l'objet (pour la liste d'objets et pour le hangar)
- Légère modification des recherches pour remonter mieux les objets


## [0.2.4]
Version SC : 4.10.1

### Ajouté
- Support de la version 4.10.1 de Star Citizen

### Corrigé
- Correction de la lecture de la quantité de ressource présent dans un cargo (int16 au lieu de int64)
- Correction de l'affichage de la conversion microSCU → SCU
- Correction de la récupération des Blueprint possédés qui ne conserve que la dernière page de données reçues de l'API
- Correction de l'affichage des lieux pour les vaisseaux stockés dans des hangars sur l'écran mon hangar


## [0.2.3]
Version SC : 4.10.0
### Ajouté
- Ajout de l'information de qualité pour les cargos
- Utilisation d'une base de données locale à la place du fichier P4K

### Corrigé
- Réduction de la quantité de RAM consommée lors du chargement des données du P4K

