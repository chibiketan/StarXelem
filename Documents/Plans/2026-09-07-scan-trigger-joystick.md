# Étude : déclencher le scan de signature par une touche clavier **ou** un bouton de joystick

> Étude préalable (pas d'implémentation). Objectif : permettre à l'utilisateur de choisir, pour déclencher la capture/scan, soit une combinaison clavier (existant), soit un bouton de joystick/HOTAS, **avec le moins d'impact possible sur l'utilisateur** : même geste de configuration, aucun changement pour ceux qui n'ont pas de joystick, aucune régression sur l'existant.

Contexte existant : `Documents/Plans/2026-09-07-scan-signature-overlay.md` — le déclenchement repose sur `IGlobalHotkeyService` (`RegisterHotKey` Win32 sur un thread dédié), configuré via le contrôle `HotkeyBox` de la page Paramètres et persisté par `ScanHotkeySettings` (registre).

---

## 1. Faisabilité technique

### 1.1 Lire un joystick sans avoir le focus

| Approche | Fonctionne sans focus | Types d'appareils | Dépendance | Complexité | Verdict |
|---|---|---|---|---|---|
| **DirectInput 8** via `Vortice.DirectInput` | Oui (`CooperativeLevel.Background \| NonExclusive`, polling de l'état matériel) | Joysticks, HOTAS, pédales, throttles, vJoy/Joystick Gremlin (vus comme des appareils normaux) | NuGet `Vortice.DirectInput` 3.8.3 (maintenu, .NET 8/9+, même auteur que Vortice.Windows) | Faible : ~100 lignes pour énumérer + poller | **Recommandé** |
| DirectInput via `SharpDX.DirectInput` 4.2 | Oui | Idem | NuGet, **non maintenu depuis 2019** | Faible | Fonctionne mais dépendance morte |
| Raw Input (`WM_INPUT` + `RIDEV_INPUTSINK`) | Oui | Tout HID | Aucune (P/Invoke) | **Élevée** : fenêtre message-only + parsing HID (`hid.dll`, `HidP_GetUsages`) par appareil | Trop coûteux pour le gain |
| XInput | Oui | Manettes Xbox uniquement | Aucune | Faible | Inadapté (HOTAS/joysticks non couverts) |
| SDL2 (`SDL2-CS`) | Oui | Tout | Native DLL SDL2 à distribuer | Moyenne | Surdimensionné |

DirectInput est exactement ce qu'utilisent Star Citizen et les outils tiers (Joystick Gremlin, vJoy) : les appareils qu'un joueur SC a configurés seront visibles. Point à vérifier en pratique : SC n'acquiert pas ses appareils en mode exclusif (c'est le cas de la quasi-totalité des jeux), sinon la lecture en arrière-plan serait bloquée pendant que le jeu tourne.

### 1.2 Différence de comportement avec le clavier — à assumer dans l'UI

- **Clavier (`RegisterHotKey`)** : le raccourci est *consommé*, le jeu ne le voit pas.
- **Joystick (DirectInput polling)** : on *observe* l'état du bouton, on ne peut pas l'intercepter. **Le jeu reçoit aussi la pression.** L'utilisateur doit donc choisir un bouton non assigné dans SC (ou accepter que l'action du jeu se déclenche en même temps). Le texte d'aide de la page Paramètres doit le dire explicitement.

### 1.3 Contraintes DirectInput à connaître

- `SetCooperativeLevel(hwnd, Background | NonExclusive)` : `IntPtr.Zero` est accepté en pratique pour ce mode (samples Vortice/SharpDX) ; sinon utiliser le handle de `MainWindow` (disponible après `Opened`). Prévoir le fallback.
- Identification stable d'un appareil : `DeviceInstance.InstanceGuid` (unique par appareil *et* par machine, survit aux redémarrages ; distingue deux sticks identiques). `ProductName` sert uniquement à l'affichage.
- Jusqu'à 128 boutons (`RawJoystickState.Buttons`) : suffisant pour tout HOTAS.
- Hot-plug : `GetDevices(DeviceClass.GameControl, AttachedOnly)` à ré-exécuter périodiquement (ex. toutes les 5 s) tant que l'appareil configuré n'est pas trouvé ; `Acquire()` à refaire après une perte (`Poll()` en échec).
- Polling : `Poll()` + `GetCurrentJoystickState()` toutes les 50 ms sur un thread dédié — coût CPU négligeable, latence < 100 ms, imperceptible pour un déclencheur. Détection **sur front montant** (transition relâché → appuyé) pour ne déclencher qu'une fois par pression, même bouton maintenu.

---

## 2. Conception UX — minimiser l'impact utilisateur

### 2.1 Principe : un seul geste, un seul contrôle

On **ne rajoute ni onglet, ni liste déroulante d'appareils, ni sélecteur "clavier / joystick"**. Le contrôle de saisie existant (`HotkeyBox`) évolue en `TriggerCaptureBox` :

1. L'utilisateur clique dans la zone → elle passe en mode capture, texte d'invite : *« Appuyez sur une touche (avec Ctrl, Alt ou Shift) ou sur un bouton de votre joystick… »*.
2. **La première entrée reçue gagne** : une combinaison clavier (comportement actuel) **ou** un bouton de n'importe quel joystick branché (nouveau).
3. Affichage résultant : `Ctrl + F9` ou `🕹 VKB Gladiator NXT — Bouton 12`.
4. `Échap` annule la capture, `Retour arrière` efface. Le bouton **Sauvegarder** applique, comme aujourd'hui.

Conséquences :
- **Sans joystick branché : strictement aucun changement** visible ni de comportement (le mode capture n'écoute simplement aucun appareil).
- Valeur par défaut inchangée (`Ctrl + F9`) : les utilisateurs existants ne voient aucune différence après mise à jour.
- Pas de nouvelle notion à apprendre : « cliquez, appuyez sur ce que vous voulez utiliser ».

### 2.2 Retours d'état, sans bruit

| Situation | Affichage (page Paramètres) |
|---|---|
| Joystick configuré et branché | `🕹 <Nom> — Bouton N` (couleur normale) |
| Joystick configuré mais **non branché** au démarrage / débranché | Même libellé grisé + bandeau discret *« Joystick non connecté — le déclencheur sera actif dès le branchement »* ; reconnexion automatique, aucune action requise |
| Aucun joystick détecté en mode capture | Rien de spécial : l'invite mentionne le joystick, la saisie clavier fonctionne |
| Bouton déjà utilisé dans SC | Impossible à détecter côté StarXelem → phrase d'aide permanente : *« Un bouton de joystick est aussi transmis au jeu : choisissez un bouton non assigné dans Star Citizen. »* |

### 2.3 Variante envisagée puis écartée : clavier **et** joystick simultanés

Autoriser deux déclencheurs actifs en même temps (un clavier + un joystick) est techniquement trivial, mais impose deux zones de saisie et une notion de « déclencheur secondaire ». La demande est « soit… soit » ; on reste sur **un seul déclencheur**, le modèle de données ci-dessous permettant d'ajouter la variante plus tard sans migration (le second déclencheur serait simplement une seconde entrée).

### 2.4 Hors périmètre (extensions possibles)

- Chapeau chinois (POV) ou axe comme déclencheur : possible avec DirectInput, mais complexifie la capture (seuils) — boutons uniquement dans un premier temps.
- Combinaison de deux boutons joystick (« modificateur ») : pas nécessaire, un bouton de HOTAS non assigné existe presque toujours.

---

## 3. Conception technique

### 3.1 Modèle de données

Remplacer `ScanHotkeySettings` par un `ScanTriggerSettings` polymorphe, **rétrocompatible** avec les clés registre existantes :

```csharp
public enum ScanTriggerKind { Keyboard, Joystick }

public sealed record ScanTriggerSettings(
    bool Enabled,
    ScanTriggerKind Kind,
    // Clavier (existant)
    Key Key, KeyModifiers Modifiers,
    // Joystick (nouveau)
    Guid? JoystickInstanceGuid, string? JoystickProductName, int JoystickButton)
{
    public static ScanTriggerSettings Default => new(true, ScanTriggerKind.Keyboard, Key.F9, KeyModifiers.Control, null, null, -1);
    public string ToDisplayString();   // "Ctrl + F9"  |  "🕹 VKB Gladiator NXT — Bouton 12"
}
```

| Clé registre | Existant ? | Défaut |
|---|---|---|
| `ScanHotkeyEnabled`, `ScanHotkeyKey`, `ScanHotkeyModifiers` | oui (inchangées) | — |
| `ScanTriggerKind` | **nouveau** | `Keyboard` (⇒ absence de clé = comportement actuel) |
| `ScanJoystickInstanceGuid`, `ScanJoystickProductName`, `ScanJoystickButton` | **nouveau** | vides |

Aucune migration : une installation existante lit `Kind = Keyboard` par défaut.

### 3.2 Services

```
IScanTriggerService  (nouveau, remplace IGlobalHotkeyService dans l'orchestrateur)
 ├─ Win32GlobalHotkeyService      (existant, inchangé)   ← Kind == Keyboard
 └─ DirectInputJoystickService    (nouveau)              ← Kind == Joystick
```

- `IScanTriggerService` : `event Triggered`, `bool TryApply(ScanTriggerSettings, out string? error)`, `Stop()`, `TriggerStatus Status` (Active / DeviceNotConnected / Disabled) — l'orchestrateur ne change presque pas : il applique les settings au service correspondant au `Kind` et arrête l'autre.
- `DirectInputJoystickService` :
  - thread dédié `"StarXelem.Joystick"`, `IsBackground = true`, boucle 50 ms ;
  - `IDirectInput8` créé une fois ; tant que `JoystickInstanceGuid` n'est pas attaché : ré-énumération toutes les 5 s, `Status = DeviceNotConnected` ;
  - quand attaché : `CreateDevice(guid)`, `SetDataFormat<RawJoystickState>()`, `SetCooperativeLevel(IntPtr.Zero, Background | NonExclusive)`, `Acquire()` ;
  - à chaque tick : `Poll()` (ré-`Acquire()` si échec, retour en `DeviceNotConnected` après N échecs), `GetCurrentJoystickState().Buttons[JoystickButton]`, front montant → `Triggered`.
  - méthode **de capture** pour l'UI : `Task<JoystickBinding?> CaptureNextButtonAsync(CancellationToken)` — énumère *tous* les appareils attachés, les acquiert, retourne le premier bouton qui passe à l'état appuyé (avec `InstanceGuid` + `ProductName` + index), puis libère les appareils non retenus.
- Mode design : `DesignScanTriggerService` inerte (comme aujourd'hui).

### 3.3 Contrôle `TriggerCaptureBox` (évolution de `HotkeyBox`)

- Conserve le comportement clavier actuel (`OnKeyDown`, filtrage via `HotkeyKeyMapping`).
- `GotFocus` → démarre `CaptureNextButtonAsync` (annulable) ; `LostFocus`/`Échap` → annule.
- Le premier des deux (touche ou bouton) affecte une `StyledProperty<ScanTriggerSettings> Trigger` (remplace `Key`/`Modifiers`) et met à jour le texte.
- Pour ne pas coupler un contrôle Avalonia au service, le contrôle expose un délégué `Func<CancellationToken, Task<JoystickBinding?>>? JoystickCaptureProvider` fourni par le ViewModel (bindable).

### 3.4 Page Paramètres

- Remplacement de `comp:HotkeyBox` par `comp:TriggerCaptureBox`, même emplacement, même style.
- Texte d'aide mis à jour (une phrase de plus sur la transmission au jeu).
- Bandeau « joystick non connecté » lié à `Status`.
- `SettingsTabViewModel` : `ScanKey`/`ScanModifiers` remplacés par une propriété `ScanTrigger` ; le reste (Sauvegarder, Tester maintenant, Tester l'overlay) inchangé.

### 3.5 Fichiers impactés (estimation)

| Fichier | Action | Taille |
|---|---|---|
| `StarXelem.csproj` | `+ PackageReference Vortice.DirectInput 3.8.3` | 1 ligne |
| `Services/Scan/ScanTriggerSettings.cs` | nouveau (remplace `ScanHotkeySettings`, qui devient un alias ou est supprimé) | ~80 |
| `Services/Scan/IScanTriggerService.cs` | nouveau | ~20 |
| `Services/Scan/DirectInputJoystickService.cs` + `DesignScanTriggerService.cs` | nouveaux | ~200 |
| `Services/Scan/Win32GlobalHotkeyService.cs` | implémente `IScanTriggerService` (renommage de l'événement) | ~10 |
| `Services/Scan/ScanSignatureOrchestrator.cs` | sélection du service selon `Kind` | ~20 |
| `Components/TriggerCaptureBox.cs` | évolution de `HotkeyBox` | ~60 |
| `ViewModels/SettingsTabViewModel.cs`, `Views/SettingsTabView.axaml` | propriété `ScanTrigger`, bandeau statut | ~40 |
| `Constants/ScanConstants.cs` | clés registre, intervalles de polling/ré-énumération | ~8 |

Ordre de grandeur : **~450 lignes, 1 dépendance, 0 migration de données**.

---

## 4. Risques et points à valider

| Risque | Probabilité | Mitigation |
|---|---|---|
| SC acquiert le joystick en exclusif (lecture arrière-plan bloquée) | Faible (rare pour un jeu) | Test réel avant implémentation : lire un bouton avec SC au premier plan. Si bloqué : Raw Input en plan B. |
| Bouton choisi déjà assigné dans SC | Moyenne | Texte d'aide explicite ; impossible à détecter côté StarXelem |
| Appareil absent au démarrage (branché après) | Fréquent (USB) | Ré-énumération périodique + statut visible + reconnexion sans action |
| Deux appareils identiques (ex. deux sticks VKB) | Possible | `InstanceGuid` les distingue ; l'affichage ajoute « (2) » si `ProductName` en double |
| `SetCooperativeLevel(IntPtr.Zero, …)` refusé sur certaines configurations | Faible | Fallback sur le handle de `MainWindow` |
| Faux déclenchement par un bouton « bavard » (rebond matériel) | Faible | Front montant + anti-rebond 150 ms |
| Impact perf pendant le jeu | Négligeable | Polling 50 ms d'un seul appareil, thread en arrière-plan |

---

## 5. Recommandation

Implémenter la solution **DirectInput (`Vortice.DirectInput`) + contrôle de capture unifié** :
- pour l'utilisateur : même geste qu'aujourd'hui (« cliquez puis appuyez ») ; aucun changement s'il n'a pas de joystick ; un seul déclencheur à la fois, clavier ou joystick ;
- pour le code : un service supplémentaire derrière une interface commune, l'orchestrateur choisit selon le `Kind` ; rétrocompatible avec les paramètres existants.

**Prérequis avant de coder** : une vérification de 10 minutes que la lecture DirectInput en arrière-plan fonctionne pendant que Star Citizen est au premier plan (un petit exécutable de test ou le service branché sur un log suffit). Si c'est confirmé, la mise en œuvre peut suivre le découpage §3.5, en une seule tâche.
