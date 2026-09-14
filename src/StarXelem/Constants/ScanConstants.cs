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

    /// <summary>Échelles successives de la passe de localisation sur la zone centrale (trouver OÙ est le nombre,
    /// pas le lire). L'étage suivant n'est tenté que si le précédent n'a produit aucune valeur : ×2 coûte ~600 ms
    /// de plus (redimensionnement + reconnaissance d'une zone 4K agrandie) mais est nécessaire sur certaines captures.</summary>
    public static readonly double[] OcrLocalizationScales = [1.0, 2.0];

    /// <summary>Échelles de la localisation de repli sur la capture entière (×2 sur une capture 4K coûte ~3 s : exclu).</summary>
    public static readonly double[] OcrFullFrameLocalizationScales = [1.0];

    /// <summary>Zone « a priori » du badge : sur 22 captures (espace et sol, head tracking compris), le badge est
    /// toujours à x ≈ 50 % et y ≈ 37–42 % de la capture. Étage intermédiaire lu à ×2 : 4× moins cher que la zone
    /// centrale ×2, tenté avant elle.</summary>
    public const double OcrPriorZoneCenterYRatio = 0.39;
    public const double OcrPriorZoneWidthRatio = 0.30;
    public const double OcrPriorZoneHeightRatio = 0.30;
    public const double OcrPriorZoneScale = 2.0;

    /// <summary>Nombre minimal de caractères « chiffre » d'un mot pour être une boîte candidate en localisation
    /// (élimine l'altimètre « 7.44 » et la boussole « 260 » qui monopolisaient les boîtes les plus proches du centre).</summary>
    public const int OcrLocalizationMinDigits = 4;

    /// <summary>Échelles de la passe de lecture sur le recadrage serré du badge. Le texte HUD fait ~15 px de
    /// haut en 4K, à la limite du moteur : aucune échelle seule n'est fiable, mais un vote sur l'ensemble
    /// (en couleur et sur le canal vert) l'est. Mesuré sur 4 captures réelles : 4/4 corrects, &lt; 80 ms.</summary>
    public static readonly double[] OcrReadingScales = [1.0, 1.5, 2.0, 2.5, 3.0, 4.0];

    /// <summary>Normalisation de contraste avant OCR : étirement des percentiles 1–99 % puis gamma. Le gamma
    /// écrase les gris moyens (sol de planète clair, fond de badge) et ne garde que le texte blanc. Mesuré sur
    /// 18 captures au sol : lecture 3/18 → 15/18. Par tuiles pour la localisation (la zone mélange cockpit sombre
    /// et planète claire), globale sur le recadrage de lecture.</summary>
    public const double OcrGamma = 3.0;
    public const int OcrNormalizeTileSize = 256;
    /// <summary>En dessous de cet écart min–max, une tuile est considérée uniforme et mise à noir.</summary>
    public const int OcrNormalizeMinRange = 16;

    /// <summary>Nombre maximal de boîtes candidates issues de la localisation à lire finement.</summary>
    public const int OcrMaxLocalizationBoxes = 4;

    /// <summary>Marge autour de la boîte localisée, en multiples de sa hauteur (horizontal / vertical). Large :
    /// l'OCR lit nettement mieux le nombre avec du contexte autour (mesuré 59 → 69 passes correctes / 108 en
    /// passant de ±4× à ±12× horizontalement).</summary>
    public const double OcrCropMarginHorizontal = 12.0;
    public const double OcrCropMarginVertical = 3.0;

    /// <summary>Reconnaissance par gabarits (<see cref="Services.Scan.DigitTemplateReader"/>) en complément de l'OCR :
    /// sa lecture entre dans les candidats avec ce poids de votes (l'orchestrateur affiche le premier candidat
    /// connu en base, donc elle sert d'arbitre quand l'OCR lit une valeur inconnue, ex. confusion 6/8).</summary>
    public const int TemplateVoteWeight = 3;
    /// <summary>Seuils de corrélation (−1..1) en dessous desquels une lecture par gabarits est ignorée.</summary>
    public const double TemplateGoodMinScore = 0.7;
    public const double TemplateGoodMeanScore = 0.85;
    /// <summary>Marges du recadrage de lecture par gabarits autour de la boîte du nombre, en fraction de sa hauteur.</summary>
    public const double TemplateCropMarginX = 0.6;
    public const double TemplateCropMarginY = 0.5;

    /// <summary>Auto-apprentissage : un vote OCR au moins aussi fort que ceci étiquette les glyphes du badge et les
    /// ajoute à la base. Un vote unanime mais faux (ex. 18,960 pour 16,960) pollue la base d'un glyphe sur 300, ce
    /// que le plus proche voisin tolère.</summary>
    public const int AutoLearnMinVotes = 8;
    public const double AutoLearnMinConfidence = 0.8;
    public const int GlyphStoreMaxPerDigit = 300;
    /// <summary>Corrélation (−1..1) au-delà de laquelle un glyphe à apprendre est un doublon d'un glyphe connu de même
    /// étiquette (ignoré), ou contredit un glyphe connu d'une autre étiquette (badge refusé : l'OCR s'est trompé).</summary>
    public const double GlyphDuplicateCorrelation = 0.995;
    public const double GlyphConflictCorrelation = 0.97;
    public const string GlyphStoreFileName = "scan-glyphs.json";

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
