namespace StarXelem.ViewModels;

/// <summary>Demande d'ouverture de l'écran Objets, avec ou sans lancement du chargement de l'inventaire.</summary>
/// <param name="LoadItems">Vrai pour lancer le chargement si l'écran le permet (connexion au jeu, pas de chargement en cours).</param>
public sealed record OpenItemsMessage(bool LoadItems);
