namespace StarXelem.Constants;

/// <summary>
/// Constantes utilisées par la fonctionnalité de scan de signature radar (capture d'écran + OCR + overlay).
/// </summary>
public static class ScanConstants
{
    /// <summary>Taille de cluster pour le minage vaisseau/surface (signature unique par minéral).</summary>
    public const int MinClusterSize = 1;
    public const int MaxClusterSize = 10;

    /// <summary>Taille de cluster pour le minage à la main (FPS) et au véhicule terrestre (GroundVehicle),
    /// où la signature est générique par catégorie et non unique par minéral.</summary>
    public const int MinLargeClusterSize = 20;
    public const int MaxLargeClusterSize = 30;

    /// <summary>Tolérance absolue admise entre la valeur OCR et la signature en base (à ajuster après validation en jeu).</summary>
    public const int SignatureTolerance = 0;

    public const int OverlayDurationMs = 5000;
    public const int OcrUpscaleFactor = 3;

    public const string SettingHotkeyEnabled = "ScanHotkeyEnabled";
    public const string SettingHotkeyKey = "ScanHotkeyKey";
    public const string SettingHotkeyModifiers = "ScanHotkeyModifiers";

    public const string DefaultHotkeyKey = "F9";
    public const string DefaultHotkeyModifiers = "Control";

    public const string StarCitizenProcessName = "StarCitizen";

    /// <summary>Variable d'environnement : si définie, les images intermédiaires (capture, pré-traitement) sont écrites dans ce dossier.</summary>
    public const string DebugDirEnvVar = "STARXELEM_SCAN_DEBUG_DIR";
}
