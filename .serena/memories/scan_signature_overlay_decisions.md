# Scan de signature radar — décisions techniques (2026-09-07)

Fonctionnalité : raccourci global → capture GDI de la fenêtre Star Citizen → OCR Windows → recherche
dans la table `MineralSignatures` → overlay borderless "N× Minéral". Plan complet :
`Documents/Plans/2026-09-07-scan-signature-overlay.md`. Table de référence : `Documents/Datafiles/MineralSignatures.md`.

## Décisions
- **OCR** : `Windows.Media.Ocr` (TFM passé à `net10.0-windows10.0.19041.0`), pas de dépendance NuGet
  supplémentaire. Nécessite le pack de langue anglaise Windows installé (sinon `ISignatureOcrService.IsAvailable=false`).
- **Capture d'écran** : GDI `BitBlt` sur la fenêtre du process `StarCitizen` (mode fenêtré sans bordure requis),
  fallback sur l'écran virtuel entier. `SkiaSharp` (2.88.9, ajouté en PackageReference explicite) pour le
  pré-traitement (isolement HSL du jaune HUD, binarisation, upscale ×3).
- **Raccourci global** : `RegisterHotKey` Win32 sur un thread dédié avec sa propre boucle `GetMessage`
  (`Services/Scan/Win32GlobalHotkeyService.cs`), fonctionne même sans focus sur StarXelem, consomme la touche.
- **Overlay** : `Window` Avalonia (`SystemDecorations=None`, `ShowActivated=False`, `Opacity=0.75`) + styles
  `WS_EX_NOACTIVATE|TOOLWINDOW|TRANSPARENT` appliqués via `SetWindowLongPtr` dans `OnOpened`.
- **Hypothèse non validée en jeu** : signature(cluster de N) = N × signature(rocher isolé), N=1..10.
  Sur une capture utilisateur, la valeur lue (80 000) ne correspond à aucun multiple ≤10 connu — à vérifier
  en jeu avant de faire confiance à la fonctionnalité. Seul `Services/Mining/MineralSignatureGenerator.cs`
  est à modifier si l'hypothèse est fausse.

## Déclencheur joystick (2026-09-07, suite)
Le scan peut être déclenché par un bouton de joystick/HOTAS (DirectInput via `Vortice.DirectInput` 3.8.3,
`Services/Scan/DirectInputJoystickService.cs`) en alternative au clavier. Modèle unifié `ScanTriggerSettings`
(`Kind` Keyboard/Joystick, `JoystickBinding` = InstanceGuid + ProductName + index bouton), interface commune
`IScanTriggerService` ; l'orchestrateur applique les paramètres aux deux services, chacun ne s'activant que
si `Kind` le concerne. Contrôle `Components/TriggerCaptureBox.cs` : au focus, capture clavier ET joystick
en parallèle, la première entrée gagne (`CaptureNextButtonAsync`).
Pièges : ne PAS filtrer sur `DeviceType.Joystick/Gamepad` (les appareils vJoy se déclarent `FirstPerson`) ;
`Poll()` renvoie `S_FALSE` (=1) sur certains appareils → tester `Result.Failure`, jamais `== Ok` ;
`SetCooperativeLevel(IntPtr.Zero, Background | NonExclusive)` fonctionne (vérifié sur vJoy). Un bouton
joystick n'est pas consommé : le jeu le reçoit aussi (documenté dans l'UI). Étude :
`Documents/Plans/2026-09-07-scan-trigger-joystick.md`.

## Piège trouvé en vérifiant sur données réelles
Deux entités p4k parasites polluaient l'extraction de signatures minérales : une avec signature de base 0
("aphorite"), une avec un nom localisé non résolu (`"<= PLACEHOLDER =>"`). Fix dans
`MineralSignatureExtractor.ExtractAsync` : exclure `signatureValue <= 0` et les noms contenant `"PLACEHOLDER"`.
Sans ce fix, la table `MineralSignatures` contenait 280 lignes au lieu de 260 (26 minéraux × 10 attendus).

## Piège Avalonia : sous-classer TextBox
`HotkeyBox : TextBox` (nouveau contrôle `Components/HotkeyBox.cs`) apparaissait invisible (pas de template/bordure)
car Avalonia résout le `ControlTheme` implicite par défaut via `StyleKeyOverride` qui vaut `GetType()` par défaut —
une sous-classe sans l'overrider perd le thème du parent. Fix : `protected override Type StyleKeyOverride => typeof(TextBox);`.
À reproduire pour tout futur contrôle qui hérite d'un contrôle Avalonia standard sans re-templater entièrement.

## Vérifié sur p4k réel (LIVE 4.10)
Rebuild complet de la base locale → `MineralSignatures` = 260 lignes exactes, valeurs identiques à
`MineableRocks.md`/`MineralSignatures.md`, collisions confirmées (19200 = 5×Aslarite/6×Savrilium, etc.).

## Extension : minage FPS (main) et GroundVehicle (véhicule terrestre) (2026-09-07, suite)
Ajout demandé par l'utilisateur : ces minerais (ex. Hadanite en FPS, Glacosite en GroundVehicle) ont une
signature **générique par catégorie** (3000 pour tout minéral FPS, 4000 pour tout GroundVehicle, cf.
Documents/Datafiles/MineableRocks.md) — impossible de distinguer le minéral exact par la signature seule.
Ils se trouvent aussi en clusters bien plus grands (20-30 rochers vs 1-10 pour vaisseau/surface).

Implémentation : `MiningKind` enum (ShipOrSurface/Fps/GroundVehicle) ajouté à `MineralBaseSignature` et à
`MineralSignatureEntity.MiningType` (string). `MineralSignatureExtractor` classe via
`RecordName.Contains(".MineableRock_Fps_"/"​.MineableRock_GroundVehicle_")`, dédoublonne par (clé, kind) —
un même minéral (Carinite) existe dans les deux catégories avec des signatures différentes, donc gardé
séparément. `MineralSignatureGenerator` route la plage de cluster par Kind
(`ScanConstants.MinLargeClusterSize`=20/`MaxLargeClusterSize`=30 pour Fps/GroundVehicle). Index unique
DbContext élargi à (MineralKey, MiningType, ClusterSize). `WindowsSignatureOcrService.MaxPlausibleSignature`
remonté à 120000 (4000×30). `DatabaseVersion` → 3. Overlay ajoute un "?" aux lignes ambiguës
(`OverlayNotificationService.ShowAsync`, basé sur `MiningType != ShipOrSurface`).

**Résultat clé — vérifié sur p4k réel** : la signature mystère `80,000` de la capture initiale de
l'utilisateur correspond **exactement** à `4000 × 20` = un cluster de 20 rochers GroundVehicle, ambigu
entre Beradom/Carinite/Feynmaline/Glacosite (les 4 minéraux GroundVehicle réels extraits). C'est la
meilleure explication disponible pour cette capture (reste à confirmer en jeu). 9 minéraux FPS réels
(Aphorite, Carinite, Carinite (Pure), Dolivine, Hadanite, Jaclium, Janalite, Sadaryx, Saldynium) + 4
GroundVehicle → 403 lignes totales en base (260 ship/surface + 143 FPS/GroundVehicle).
