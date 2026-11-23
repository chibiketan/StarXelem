namespace StarXelem.Models;

/// <summary>
/// Objet possédé susceptible de servir de matériau à un blueprint : ressource en conteneur ou objet à l'unité (gemme…).
/// </summary>
/// <param name="ItemId">Identifiant de l'objet dans l'inventaire du jeu.</param>
/// <param name="ClassRecordId">Classe de l'objet (RecordId de ScItems), comparée aux coûts « Item ».</param>
/// <param name="ResourceRef">Ressource contenue d'après la classe de l'objet, comparée aux coûts « Resource ».</param>
/// <param name="Quality">Qualité de la matière (0 à 1000), ou null si l'objet n'en porte pas.</param>
/// <param name="ResourceScu">Quantité de matière contenue, en SCU (0 pour un objet à l'unité).</param>
/// <param name="Count">Nombre d'éléments de la pile (gemmes, minerais à l'unité).</param>
/// <param name="Location">Emplacement résolu, ou null tant que la résolution n'est pas terminée.</param>
public sealed record OwnedMaterialLot(
    ulong ItemId,
    string? ClassRecordId,
    string? ResourceRef,
    ushort? Quality,
    double ResourceScu,
    int Count,
    ItemLocationDisplay? Location);
