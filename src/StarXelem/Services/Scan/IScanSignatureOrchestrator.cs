namespace StarXelem.Services.Scan;

/// <summary>
/// Enchaîne, depuis la pression du raccourci global, la capture d'écran, l'OCR, la recherche en base et
/// l'affichage de l'overlay de résultat.
/// </summary>
public interface IScanSignatureOrchestrator
{
    /// <summary>Charge les paramètres du raccourci et l'applique. À appeler une fois au démarrage de l'application.</summary>
    Task StartAsync();

    /// <summary>Sauvegarde et applique de nouveaux paramètres de raccourci (appelé depuis la page Paramètres).</summary>
    Task<(bool Success, string? Error)> ApplySettingsAsync(ScanHotkeySettings settings);

    /// <summary>Exécute une fois le pipeline complet (capture → OCR → recherche → overlay). Utilisable par le raccourci ou un bouton "Tester".</summary>
    Task RunOnceAsync();

    string? LastError { get; }
}
