namespace StarXelem.Models;

/// <summary>
/// Une arme FPS présentée dans l'écran dédié. Représente un modèle d'arme, toutes variantes
/// de livrée confondues, avec les statistiques de son tir principal et, le cas échéant, de son
/// tir secondaire.
/// </summary>
public class FpsWeaponModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string TechnicalName { get; init; }

    /// <summary>Classe brute issue des données : rifle, pistol, smg…</summary>
    public required string WeaponClass { get; init; }

    public string? Manufacturer { get; init; }

    public int? Size { get; init; }

    public int? Grade { get; init; }

    public int VariantCount { get; init; }

    /// <summary>Noms des variantes de livrée, une par ligne, destinés à l'infobulle.</summary>
    public string? VariantNames { get; init; }

    public int? MagazineSize { get; init; }

    public float? RepoolUnstowDuration { get; init; }

    public int? RepoolBulletsPerSecond { get; init; }

    public float? RepoolFullMagMergeDuration { get; init; }

    /* ---- Tir principal ---- */

    public string? PrimaryModeName { get; init; }

    public string? PrimaryDamageType { get; init; }

    public float? PrimaryDamagePerShot { get; init; }

    /// <summary>Dégâts d'explosion par tir. Null quand la munition n'explose pas.</summary>
    public float? PrimaryExplosiveDamage { get; init; }

    public int? PrimaryPelletCount { get; init; }

    public int? PrimaryFireRate { get; init; }

    /// <summary>Vitesse du projectile en m/s.</summary>
    public float? PrimaryProjectileSpeed { get; init; }

    public float? PrimaryMaxRange { get; init; }

    public float? PrimaryEffectiveRange { get; init; }

    public float? PrimaryDamageFloorRange { get; init; }

    /* ---- Tir secondaire ---- */

    public bool HasSecondaryFire { get; init; }

    public string? SecondaryModeName { get; init; }

    public string? SecondaryDamageType { get; init; }

    public float? SecondaryDamagePerShot { get; init; }

    /// <summary>Dégâts d'explosion par tir. Null quand la munition n'explose pas.</summary>
    public float? SecondaryExplosiveDamage { get; init; }

    public int? SecondaryPelletCount { get; init; }

    public int? SecondaryFireRate { get; init; }

    /// <summary>Vitesse du projectile en m/s.</summary>
    public float? SecondaryProjectileSpeed { get; init; }

    public float? SecondaryMaxRange { get; init; }

    public float? SecondaryEffectiveRange { get; init; }

    public float? SecondaryDamageFloorRange { get; init; }

    /* ---- Libellés d'affichage ---- */

    public string WeaponClassLabel => WeaponClass.ToLowerInvariant() switch
    {
        "rifle" => "Fusil d'assaut",
        "pistol" => "Pistolet",
        "smg" => "Pistolet-mitrailleur",
        "shotgun" => "Fusil à pompe",
        "sniper" => "Fusil de précision",
        "lmg" => "Mitrailleuse légère",
        "hmg" => "Mitrailleuse lourde",
        "glauncher" => "Lance-grenades",
        "crossbow" => "Arbalète",
        "special" => "Arme spéciale",
        _ => WeaponClass
    };

    public string PrimaryDamageTypeLabel => FormatDamageType(PrimaryDamageType);

    public string SecondaryDamageTypeLabel => FormatDamageType(SecondaryDamageType);

    /// <summary>
    /// Libellé du tir secondaire pour l'en-tête de groupe : « — » quand l'arme n'a qu'un mode.
    /// </summary>
    public string SecondarySummary => HasSecondaryFire
        ? $"{SecondaryModeName ?? "Secondaire"} · {SecondaryDamageTypeLabel}"
        : "—";

    /// <summary>
    /// Texte de l'infobulle listant les variantes de livrée regroupées sur cette ligne.
    /// </summary>
    public string VariantTooltip => string.IsNullOrWhiteSpace(VariantNames)
        ? Name
        : $"{VariantCount} variante(s) :{Environment.NewLine}{VariantNames}";

    /// <summary>
    /// Dégâts par tir les plus élevés parmi les modes disponibles, utilisés pour le tri
    /// et comme repère de bornes du filtre.
    /// </summary>
    public float? BestDamagePerShot => Max(
        Max(PrimaryDamagePerShot, SecondaryDamagePerShot),
        Max(PrimaryExplosiveDamage, SecondaryExplosiveDamage));

    /// <summary>
    /// Portée d'efficacité la plus élevée parmi les modes disponibles.
    /// </summary>
    public float? BestEffectiveRange => Max(PrimaryEffectiveRange, SecondaryEffectiveRange);

    /// <summary>
    /// Indique si l'un des modes de tir a des dégâts par tir dans l'intervalle demandé.
    /// </summary>
    public bool MatchesDamageRange(double min, double max) =>
        IsInRange(PrimaryDamagePerShot, min, max) || IsInRange(SecondaryDamagePerShot, min, max)
        || IsInRange(PrimaryExplosiveDamage, min, max) || IsInRange(SecondaryExplosiveDamage, min, max);

    /// <summary>
    /// Indique si l'un des modes de tir a une portée d'efficacité dans l'intervalle demandé.
    /// Les armes dont aucun mode n'a de portée connue ne correspondent jamais : c'est à l'appelant
    /// de les réintégrer explicitement s'il le souhaite.
    /// </summary>
    public bool MatchesEffectiveRange(double min, double max) =>
        IsInRange(PrimaryEffectiveRange, min, max) || IsInRange(SecondaryEffectiveRange, min, max);

    /// <summary>
    /// Vrai quand aucun mode de tir n'expose de portée d'efficacité (munition sans chute de dégâts).
    /// </summary>
    public bool HasNoKnownEffectiveRange => PrimaryEffectiveRange is null && SecondaryEffectiveRange is null;

    /* ---- Chaînes d'affichage (« — » quand la donnée est absente) ---- */

    /// <summary>
    /// Dégâts directs, suivis entre parenthèses des dégâts d'explosion quand la munition en a :
    /// l'Animus affiche « 0 (150) », le Boomtube « 20 (41000) », un fusil classique « 80 ».
    /// </summary>
    public string PrimaryDamageDisplay => FormatDamage(PrimaryDamagePerShot, PrimaryExplosiveDamage);

    public string SecondaryDamageDisplay => FormatDamage(SecondaryDamagePerShot, SecondaryExplosiveDamage);

    public string PrimaryFireRateDisplay => FormatNumber(PrimaryFireRate);

    public string SecondaryFireRateDisplay => FormatNumber(SecondaryFireRate);

    public string PrimaryProjectileSpeedDisplay => FormatSpeed(PrimaryProjectileSpeed);

    public string SecondaryProjectileSpeedDisplay => FormatSpeed(SecondaryProjectileSpeed);

    public string PrimaryEffectiveRangeDisplay => FormatDistance(PrimaryEffectiveRange);

    public string SecondaryEffectiveRangeDisplay => FormatDistance(SecondaryEffectiveRange);

    public string PrimaryMaxRangeDisplay => FormatDistance(PrimaryMaxRange);

    public string SecondaryMaxRangeDisplay => FormatDistance(SecondaryMaxRange);

    public string PrimaryDamageFloorRangeDisplay => FormatDistance(PrimaryDamageFloorRange);

    public string PrimaryModeDisplay => PrimaryModeName ?? "—";

    public string SecondaryModeDisplay => SecondaryModeName ?? "—";

    public string MagazineSizeDisplay => FormatNumber(MagazineSize);

    /// <summary>Nombre de projectiles par tir, affiché seulement au-delà de 1 (gerbes de plombs).</summary>
    public string PrimaryPelletDisplay => PrimaryPelletCount is > 1 ? $"×{PrimaryPelletCount}" : string.Empty;

    public string SecondaryPelletDisplay => SecondaryPelletCount is > 1 ? $"×{SecondaryPelletCount}" : string.Empty;

    /// <summary>
    /// Temps d'extraction d'un chargeur depuis le sac à dos, avec la cadence d'extraction.
    /// Remplace la « durée de recharge » qui n'existe pas dans les données du jeu.
    /// </summary>
    public string RepoolDisplay
    {
        get
        {
            if (RepoolUnstowDuration is null && RepoolBulletsPerSecond is null)
            {
                return "—";
            }

            var unstow = RepoolUnstowDuration is null ? "—" : $"{FormatNumber(RepoolUnstowDuration)} s";
            return RepoolBulletsPerSecond is null
                ? unstow
                : $"{unstow} · {RepoolBulletsPerSecond}/s";
        }
    }

    public string VariantCountDisplay => VariantCount > 1 ? VariantCount.ToString() : string.Empty;

    public bool HasMultipleVariants => VariantCount > 1;

    private static string FormatDamage(float? direct, float? explosive) =>
        explosive is null
            ? FormatNumber(direct)
            : $"{FormatNumber(direct)} ({FormatNumber(explosive)})";

    private static string FormatNumber(float? value) =>
        value is null ? "—" : value.Value.ToString("0.##");

    private static string FormatNumber(int? value) =>
        value is null ? "—" : value.Value.ToString();

    private static string FormatDistance(float? value) =>
        value is null ? "—" : $"{value.Value.ToString("0.##")} m";

    private static string FormatSpeed(float? value) =>
        value is null ? "—" : $"{value.Value.ToString("0.##")} m/s";

    private static string FormatDamageType(string? damageType) => damageType switch
    {
        "Physical" => "Physique",
        "Energy" => "Énergie",
        "Distortion" => "Distorsion",
        "Thermal" => "Thermique",
        "Biochemical" => "Biochimique",
        "Stun" => "Assommant",
        _ => "—"
    };

    private static bool IsInRange(float? value, double min, double max) =>
        value is not null && value >= min && value <= max;

    private static float? Max(float? left, float? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Math.Max(left.Value, right.Value);
    }
}
