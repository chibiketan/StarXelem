namespace StarXelem.Services.Scan;

public enum ScanTriggerStatus
{
    /// <summary>Le service n'est pas concerné par les paramètres courants (désactivé ou autre type de déclencheur).</summary>
    Disabled,
    Active,
    /// <summary>Joystick configuré mais non branché : le déclencheur devient actif automatiquement au branchement.</summary>
    DeviceNotConnected,
    Error,
}

/// <summary>
/// Source de déclenchement du scan de signature. Chaque implémentation ne s'active que si
/// <see cref="ScanTriggerSettings.Kind"/> la concerne, et se désactive sinon : l'orchestrateur applique
/// les mêmes paramètres à toutes les implémentations.
/// </summary>
public interface IScanTriggerService : IDisposable
{
    event EventHandler? Triggered;
    event EventHandler? StatusChanged;

    ScanTriggerStatus Status { get; }

    /// <summary>(Ré)applique les paramètres. Retourne false + message si le déclencheur ne peut pas être installé.</summary>
    bool TryApply(ScanTriggerSettings settings, out string? error);

    void Stop();
}

/// <summary>Déclencheur clavier global (RegisterHotKey).</summary>
public interface IGlobalHotkeyService : IScanTriggerService
{
}

/// <summary>Déclencheur par bouton de joystick/HOTAS (DirectInput, lecture en arrière-plan).</summary>
public interface IJoystickTriggerService : IScanTriggerService
{
    /// <summary>
    /// Attend la prochaine pression d'un bouton sur n'importe quel joystick branché (sert à la saisie dans les
    /// Paramètres). Retourne null si annulé ou si aucun joystick n'est disponible.
    /// </summary>
    Task<JoystickBinding?> CaptureNextButtonAsync(CancellationToken cancellationToken);
}
