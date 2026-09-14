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

    /// <summary>Zone centrale de la capture (proportion de la largeur/hauteur) dans laquelle le badge de
    /// signature est cherché en priorité. Large pour tolérer le head tracking, sans réglage utilisateur.</summary>
    public const double OcrRoiWidthRatio = 0.6;
    public const double OcrRoiHeightRatio = 0.5;

    /// <summary>Échelles successives de la passe de localisation (trouver OÙ est le nombre, pas le lire).
    /// L'étage suivant n'est tenté que si le précédent n'a produit aucune valeur : ×2 coûte ~600 ms de plus
    /// (redimensionnement + reconnaissance d'une zone 4K agrandie) mais est nécessaire sur certaines captures.</summary>
    public static readonly double[] OcrLocalizationScales = [1.0, 2.0];

    /// <summary>Échelles de la passe de lecture sur le recadrage serré du badge. Le texte HUD fait ~15 px de
    /// haut en 4K, à la limite du moteur : aucune échelle seule n'est fiable, mais un vote sur l'ensemble
    /// (en couleur et sur le canal vert) l'est. Mesuré sur 4 captures réelles : 4/4 corrects, &lt; 80 ms.</summary>
    public static readonly double[] OcrReadingScales = [1.0, 1.5, 2.0, 2.5, 3.0, 4.0];

    /// <summary>Nombre maximal de boîtes candidates issues de la localisation à lire finement.</summary>
    public const int OcrMaxLocalizationBoxes = 3;

    /// <summary>Marge autour de la boîte localisée, en multiples de sa hauteur (horizontal / vertical).</summary>
    public const double OcrCropMarginHorizontal = 4.0;
    public const double OcrCropMarginVertical = 3.0;

    /// <summary>Nombre de voisins en base proposés quand la signature lue est inconnue.</summary>
    public const int UnknownSignatureNeighbours = 2;

    public const string SettingHotkeyEnabled = "ScanHotkeyEnabled";
    public const string SettingHotkeyKey = "ScanHotkeyKey";
    public const string SettingHotkeyModifiers = "ScanHotkeyModifiers";
    public const string SettingTriggerKind = "ScanTriggerKind";
    public const string SettingJoystickInstanceGuid = "ScanJoystickInstanceGuid";
    public const string SettingJoystickProductName = "ScanJoystickProductName";
    public const string SettingJoystickButton = "ScanJoystickButton";

    /// <summary>Période de lecture de l'état du joystick (DirectInput) sur le thread de déclenchement.</summary>
    public const int JoystickPollIntervalMs = 50;
    /// <summary>Période de ré-énumération des joysticks tant que l'appareil configuré n'est pas branché.</summary>
    public const int JoystickReenumerateIntervalMs = 5000;
    /// <summary>Anti-rebond entre deux déclenchements par bouton de joystick.</summary>
    public const int JoystickDebounceMs = 150;

    public const string DefaultHotkeyKey = "F9";
    public const string DefaultHotkeyModifiers = "Control";

    public const string StarCitizenProcessName = "StarCitizen";

    /// <summary>Variable d'environnement : si définie, les images intermédiaires (capture, pré-traitement) sont écrites dans ce dossier.</summary>
    public const string DebugDirEnvVar = "STARXELEM_SCAN_DEBUG_DIR";
}
