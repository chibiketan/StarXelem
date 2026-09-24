# Extraction des Missions depuis les Logs Star Citizen

## Format du Log

Ligne type : `<YYYY-MM-DDTHH:MM:SS.mmmZ> [Niveau] <Emetteur> Message [Tags]`

Horodatage en UTC. Heures locales = UTC +1/+2 selon heure d'été.

## Marqueurs de Mission

### Acceptation (Début)

- **Emetteur** : `SHUDEvent_OnNotification`
- **Mot-clé** : `Contract Accepted` dans le message
- **MissionId** : UUID présent entre crochets `[xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx]`
- **Nom du template** : présent dans la ligne d'erreur `CommsNotifications` ou `CSCPlayerMissionLog::MissionStartCommsNotification` juste avant, sous forme `ContractTemplate[...][NOM_DU_TEMPLATE]`

Exemple :
```
<2026-05-28T10:09:46.971Z> [Error] <CSCPlayerMissionLog::MissionStartCommsNotification> ... ContractTemplate[...][HaulCargo_SingleToMulti4_RefinedOre_Quartz_Stanton_SupplyGrade1]. MissionId: 13c9e477-0cd1-49d1-9dc7-5c1ad76c1f4f
<2026-05-28T10:09:46.975Z> [Notice] <SHUDEvent_OnNotification> Added notification "Contract Accepted:  Senior Rank - Medium Cargo Haul : " [6] ... MissionId: [13c9e477-0cd1-49d1-9dc7-5c1ad76c1f4f]
```

### Complétion (Fin)

- **Emetteur** : `MissionEnded`
- **Mot-clé** : `MISSION_STATE_COMPLETED`
- **MissionId** : UUID dans `mission_id`

Exemple :
```
<2026-05-28T11:06:28.271Z> [Notice] <MissionEnded> Received MissionEnded push message for: mission_id 13c9e477-0cd1-49d1-9dc7-5c1ad76c1f4f - mission_state MISSION_STATE_COMPLETED
```

## Données Extraites par Mission

| Champ | Source | Détail |
|---|---|---|
| MissionId (UUID) | `MissionId` dans les deux marqueurs | Clé de corrélation début/fin |
| Nom du template | `ContractTemplate[...][TEMPLATE]` ligne d'erreur avant acceptation | Ex: `HaulCargo_SingleToMulti4_RefinedOre_Quartz_Stanton_SupplyGrade1` |
| Heure de début | Horodatage ligne `Contract Accepted` | UTC |
| Heure de fin | Horodatage ligne `MissionEnded` | UTC |
| Durée | Différence fin - début | A calculer |

## Autres Etats de Mission

Le marqueur `MissionEnded` peut contenir d'autres valeurs que `MISSION_STATE_COMPLETED` (ex: `MISSION_STATE_FAILED`, `MISSION_STATE_ABANDONED`). Les vérifier au cas par cas.

## Méthode de Recherche

1. Chercher `Contract Accepted` pour trouver les débuts de mission avec leur MissionId et template
2. Chercher `MissionEnded` pour trouver les fins de mission avec leur MissionId et état (y compris abandon)
3. Corréler via MissionId pour calculer les durées
