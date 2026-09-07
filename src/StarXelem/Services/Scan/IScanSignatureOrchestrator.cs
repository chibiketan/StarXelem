namespace StarXelem.Services.Scan;

/// <summary>
/// Enchaîne, depuis le déclencheur configuré (raccourci clavier ou bouton de joystick), la capture d'écran,
/// l'OCR, la recherche en base et l'affichage de l'overlay de résultat.
/// </summary>
public interface IScanSignatureOrchestrator
{
    /// <summary>Charge les paramètres du déclencheur et l'applique. À appeler une fois au démarrage de l'application.</summary>
    Task StartAsync();

    /// <summary>Sauvegarde et applique de nouveaux paramètres de déclencheur (appelé depuis la page Paramètres).</summary>
    Task<(bool Success, string? Error)> ApplySettingsAsync(ScanTriggerSettings settings);

    /// <summary>Exécute une fois le pipeline complet (capture → OCR → recherche → overlay). Utilisable par le déclencheur ou un bouton "Tester".</summary>
    Task RunOnceAsync();

    /// <summary>Arrête tous les déclencheurs (à la fermeture de l'application).</summary>
    void StopTriggers();

    /// <summary>Suspend temporairement le déclencheur actif (pendant la saisie d'un nouveau déclencheur dans les Paramètres).</summary>
    void PauseTriggers();

    /// <summary>Réinstalle le dernier déclencheur appliqué après une pause.</summary>
    void ResumeTriggers();

    /// <summary>État du déclencheur correspondant au type configuré.</summary>
    ScanTriggerStatus TriggerStatus { get; }

    event EventHandler? TriggerStatusChanged;

    string? LastError { get; }
}
