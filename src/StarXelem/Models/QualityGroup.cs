namespace StarXelem.Models;

/// <summary>
/// Lots d'un matériau ayant la même qualité, quel que soit leur emplacement.
/// </summary>
/// <param name="Quality">Qualité exacte (0 à 1000), ou null si l'objet n'en porte pas.</param>
/// <param name="Quantity">Quantité totale du groupe.</param>
/// <param name="IsExcluded">Vrai si la qualité est sous le minimum exigé par le blueprint.</param>
/// <param name="Places">Répartition par emplacement, de la plus grande quantité à la plus petite.</param>
public sealed record QualityGroup(int? Quality, double Quantity, bool IsExcluded, IReadOnlyList<MaterialPlace> Places);
