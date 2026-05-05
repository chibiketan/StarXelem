namespace StarXelem.Services;

public enum GrpcConnectionStatus
{
    /// <summary>Le fichier loginData.json est absent — le jeu n'est pas lancé.</summary>
    Disconnected,

    /// <summary>Connexion en cours après sélection d'un environnement P4K.</summary>
    Connecting,

    /// <summary>Le token est expiré ou une erreur gRPC est survenue.</summary>
    Error,

    /// <summary>Le client est connecté au jeu mais le joueur n'est pas sur une shard.</summary>
    Connected,

    /// <summary>Le client est connecté et le joueur est présent sur une shard.</summary>
    InGame
}
