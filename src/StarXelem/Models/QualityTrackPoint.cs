namespace StarXelem.Models;

/// <summary>
/// Point de la piste de qualité : un groupe de lots d'une même qualité.
/// </summary>
/// <param name="Quality">Qualité exacte (0 à 1000), position du point sur la piste.</param>
/// <param name="Quantity">Quantité du groupe, qui fixe la taille du point.</param>
/// <param name="IsExcluded">Vrai si la qualité est sous le minimum : point grisé et non sélectionnable.</param>
/// <param name="Label">Étiquette au-dessus du point (« 886 · 11 »).</param>
public sealed record QualityTrackPoint(int Quality, double Quantity, bool IsExcluded, string Label);
