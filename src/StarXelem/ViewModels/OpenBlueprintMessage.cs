namespace StarXelem.ViewModels;

/// <summary>Demande d'ouverture de l'écran Blueprints sur un blueprint précis (SelfId du blueprint).</summary>
public sealed record OpenBlueprintMessage(string BlueprintId);
