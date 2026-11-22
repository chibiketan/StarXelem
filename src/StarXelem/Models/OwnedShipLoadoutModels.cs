namespace StarXelem.Models;

/// <summary>Provenance du loadout affiché pour un vaisseau possédé.</summary>
public enum OwnedShipLoadoutSource
{
    /// <summary>Objets réellement montés, lus sur le compte du joueur.</summary>
    Real,

    /// <summary>
    /// Dernier loadout sauvegardé à l'ASOP (snapshot CUSTOMIZED, sinon DEFAULT) : le vaisseau est livré mais absent de l'univers (détruit, rangé
    /// hors de portée). Les ports que le snapshot ne suit pas sont complétés par l'équipement d'origine.
    /// </summary>
    Saved,

    /// <summary>Équipement de série du Data.p4k : le vaisseau n'a pas été retrouvé dans l'univers.</summary>
    Origin
}

/// <summary>Composant d'un vaisseau possédé, avec son écart éventuel par rapport au loadout d'origine.</summary>
public class OwnedShipComponentModel : P4kShipComponentModel
{
    /// <summary>Vrai si le composant diffère de celui d'origine sur ce port.</summary>
    public bool IsModified { get; init; }

    /// <summary>Vrai si le port est vide alors qu'il est équipé d'origine.</summary>
    public bool IsRemoved { get; init; }

    /// <summary>Composant d'origine sur ce port, pour l'infobulle d'un composant modifié.</summary>
    public string? OriginName { get; init; }

    /// <summary>Infobulle : port et, pour un composant modifié, composant d'origine.</summary>
    public string Tooltip => IsModified && OriginName != null ? $"{PortName}\nD'origine : {OriginName}" : PortName;
}

/// <summary>Groupe de composants d'un même type, affiché dans une carte.</summary>
public record OwnedShipComponentSection(string Title, IReadOnlyList<OwnedShipComponentModel> Components)
{
    /// <summary>Vrai si au moins un composant de la carte diffère de l'origine.</summary>
    public bool HasModified => Components.Any(component => component.IsModified);
}

/// <summary>Ligne du tableau « Armes et missiles » : une arme, ou un lanceur avec ses missiles, sur un point d'emport.</summary>
public class OwnedShipWeaponRow
{
    /// <summary>Nom du point d'emport de premier niveau (port du vaisseau).</summary>
    public required string HardpointName { get; init; }

    /// <summary>Nom technique du point d'emport de premier niveau.</summary>
    public required string PortName { get; init; }

    /// <summary>Nom de l'arme, du lanceur ou du missile.</summary>
    public required string EquipmentName { get; init; }

    /// <summary>Supports intermédiaires (tourelle, support d'arme…), ou <c>null</c> si l'équipement est monté directement.</summary>
    public string? MountLabel { get; init; }

    public int Size { get; init; }

    /// <summary>Type d'arme, ou contenu d'un lanceur (« 4 × Arrester III »).</summary>
    public string? TypeLabel { get; init; }

    public float? AlphaDamage { get; init; }

    /// <summary>Vrai pour un missile, une bombe ou leur lanceur.</summary>
    public bool IsMissile { get; init; }

    public bool IsModified { get; init; }

    /// <summary>Vrai si le point d'emport est vide alors qu'il est équipé d'origine.</summary>
    public bool IsRemoved { get; init; }

    /// <summary>Équipement d'origine sur ce port, pour l'infobulle d'une ligne modifiée.</summary>
    public string? OriginName { get; init; }

    public bool HasMount => !string.IsNullOrEmpty(MountLabel);
    public string SizeLabel => Size > 0 ? $"S{Size}" : "—";
    public string TypeDisplay => string.IsNullOrEmpty(TypeLabel) ? "—" : TypeLabel;
    public string DamageLabel => AlphaDamage is > 0 ? AlphaDamage.Value.ToString("N1") : "—";
    public string Tooltip => IsModified && OriginName != null ? $"{PortName}\nD'origine : {OriginName}" : PortName;
}

/// <summary>Loadout d'un vaisseau possédé, prêt à afficher.</summary>
public class OwnedShipLoadoutModel
{
    public required OwnedShipLoadoutSource Source { get; init; }
    public required IReadOnlyList<OwnedShipComponentSection> ComponentSections { get; init; }
    public required IReadOnlyList<OwnedShipWeaponRow> Weapons { get; init; }

    /// <summary>Nombre de composants montés (hors ports vides).</summary>
    public int ComponentCount => ComponentSections.Sum(section => section.Components.Count(component => !component.IsRemoved));

    /// <summary>Nombre d'armes montées.</summary>
    public int WeaponCount => Weapons.Count(row => !row.IsMissile && !row.IsRemoved);

    /// <summary>Nombre de lignes de missiles ou bombes.</summary>
    public int MissileCount => Weapons.Count(row => row.IsMissile && !row.IsRemoved);

    /// <summary>Nombre d'écarts avec le loadout d'origine (toujours 0 pour un loadout d'origine).</summary>
    public int ModifiedCount => ComponentSections.Sum(section => section.Components.Count(component => component.IsModified))
                                + Weapons.Count(row => row.IsModified);

    public static OwnedShipLoadoutModel Empty(OwnedShipLoadoutSource source) => new()
    {
        Source = source,
        ComponentSections = [],
        Weapons = []
    };
}
