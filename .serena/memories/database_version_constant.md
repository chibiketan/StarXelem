# Version de la base de données locale

- Constante `DatabaseConstants.DatabaseVersion` (int) dans `src/StarXelem/Constants/DatabaseConstants.cs`.
- **RÈGLE : toute modification du schéma de la base de données locale ou de la façon dont les données sont chargées (colonnes, tables, logique de peuplement dans `LocalDatabaseService`) DOIT s'accompagner d'un incrément de `DatabaseConstants.DatabaseVersion`.**
- La valeur est persistée dans les settings (registre, clé littérale `"DatabaseVersion"`) via `ISettingsService`, sauvegardée automatiquement à la fin de `LocalDatabaseService.RebuildDbAsync` après un rebuild réussi (donc aussi valable pour le CLI `StarXelem.cli.testdb` qui appelle `RebuildDbAsync` directement).
- `LocalDatabaseService.NeedsRebuildCheckAsync()` compare la version stockée à `DatabaseConstants.DatabaseVersion` (en plus de la comparaison `P4KVersion` existante) ; absente ou différente → rebuild déclenché automatiquement au démarrage de l'app via `EnsureDbAsync`/`RunRebuildWithPopupAsync` dans `MainWindowViewModel`.
