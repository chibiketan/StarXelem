using System.Text.Json;

using Google.Protobuf;
using Sc.External.Services.Entitlement.V2;
using Sc.External.Services.Entitygraph.V1;

using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Sérialisation des données mises en cache par <see cref="IGrpcDataCache"/> : les réponses gRPC gardent leur format protobuf natif,
/// sans perte et tolérant aux champs ajoutés par une nouvelle version du jeu ; les données calculées localement passent par JSON.
/// </summary>
public static class GrpcCachePayload
{
    private const byte HasNodeProperties = 1;
    private const byte HasSnapshot = 2;
    private const byte HasEdge = 4;
    private const byte HasClassProperties = 8;

    private const byte HasStowContext = 1;
    private const byte HasEntityProperties = 2;
    private const byte HasResolvedLocation = 4;

    /// <summary>
    /// Sérialise une liste de messages protobuf, chacun précédé de sa taille.
    /// </summary>
    /// <param name="messages">Messages à sérialiser.</param>
    /// <returns>Le contenu sérialisé.</returns>
    public static byte[] WriteMessages(IEnumerable<IMessage> messages)
    {
        using var stream = new MemoryStream();
        foreach (var message in messages)
        {
            message.WriteDelimitedTo(stream);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Relit une liste de messages écrite par <see cref="WriteMessages"/>.
    /// </summary>
    /// <param name="payload">Contenu sérialisé.</param>
    /// <param name="parser">Lecteur du type de message.</param>
    /// <typeparam name="T">Type de message.</typeparam>
    /// <returns>Les messages relus.</returns>
    public static List<T> ReadMessages<T>(byte[] payload, MessageParser<T> parser) where T : IMessage<T>
    {
        var messages = new List<T>();
        using var stream = new MemoryStream(payload, writable: false);
        while (stream.Position < stream.Length)
        {
            messages.Add(parser.ParseDelimitedFrom(stream));
        }

        return messages;
    }

    /// <summary>
    /// Sérialise des résultats de recherche d'objets : pour chacun, un octet indiquant les parties présentes, puis ces parties.
    /// </summary>
    /// <param name="items">Résultats à sérialiser.</param>
    /// <returns>Le contenu sérialisé.</returns>
    public static byte[] WriteItems(IEnumerable<EntityItemQueryResult> items)
    {
        using var stream = new MemoryStream();
        foreach (var item in items)
        {
            WriteItem(stream, item);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Relit des résultats de recherche d'objets écrits par <see cref="WriteItems"/>.
    /// </summary>
    /// <param name="payload">Contenu sérialisé.</param>
    /// <returns>Les résultats relus.</returns>
    public static List<EntityItemQueryResult> ReadItems(byte[] payload)
    {
        var items = new List<EntityItemQueryResult>();
        using var stream = new MemoryStream(payload, writable: false);
        while (stream.Position < stream.Length)
        {
            items.Add(ReadItem(stream));
        }

        return items;
    }

    /// <summary>
    /// Sérialise les vaisseaux de l'écran Mon hangar avec tout ce que le serveur a renvoyé pour eux (droit, rangement, entité et objets
    /// montés) et leur emplacement résolu. Les noms de classe ne sont pas gardés : ils se relisent dans la base locale.
    /// </summary>
    /// <param name="ships">Vaisseaux à sérialiser.</param>
    /// <returns>Le contenu sérialisé.</returns>
    public static byte[] WriteShips(IEnumerable<SpaceshipModel> ships)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var ship in ships)
        {
            var flags = (byte)((ship.StowContext != null ? HasStowContext : 0)
                               | (ship.EntityProperties != null ? HasEntityProperties : 0)
                               | (ship.ResolvedLocation != null ? HasResolvedLocation : 0));
            writer.Write(flags);
            ship.Entitlement.WriteDelimitedTo(stream);
            ship.StowContext?.WriteDelimitedTo(stream);
            if (ship.EntityProperties != null)
            {
                WriteItem(stream, ship.EntityProperties);
                writer.Write7BitEncodedInt(ship.AttachedEntities.Count);
                foreach (var attached in ship.AttachedEntities)
                {
                    WriteItem(stream, attached);
                }
            }

            if (ship.ResolvedLocation != null)
            {
                writer.Write(JsonSerializer.Serialize(ship.ResolvedLocation));
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Relit des vaisseaux écrits par <see cref="WriteShips"/>.
    /// </summary>
    /// <param name="payload">Contenu sérialisé.</param>
    /// <returns>Les vaisseaux relus, sans nom de classe.</returns>
    public static List<SpaceshipModel> ReadShips(byte[] payload)
    {
        var ships = new List<SpaceshipModel>();
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream);
        while (stream.Position < stream.Length)
        {
            var flags = reader.ReadByte();
            var ship = new SpaceshipModel(Entitlement.Parser.ParseDelimitedFrom(stream));
            if ((flags & HasStowContext) != 0)
            {
                ship.StowContext = EntityStowContext.Parser.ParseDelimitedFrom(stream);
            }

            if ((flags & HasEntityProperties) != 0)
            {
                ship.EntityProperties = ReadItem(stream);
                var attachedCount = reader.Read7BitEncodedInt();
                var attached = new List<EntityItemQueryResult>(attachedCount);
                for (var index = 0; index < attachedCount; index++)
                {
                    attached.Add(ReadItem(stream));
                }

                ship.AttachedEntities = attached;
            }

            if ((flags & HasResolvedLocation) != 0)
            {
                ship.ResolvedLocation = JsonSerializer.Deserialize<ResolvedLocation>(reader.ReadString());
            }

            ships.Add(ship);
        }

        return ships;
    }

    /// <summary>
    /// Sérialise une donnée calculée localement (emplacements résolus…).
    /// </summary>
    /// <param name="value">Donnée à sérialiser.</param>
    /// <typeparam name="T">Type de la donnée.</typeparam>
    /// <returns>Le contenu sérialisé en JSON UTF-8.</returns>
    public static byte[] WriteJson<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    /// <summary>
    /// Relit une donnée écrite par <see cref="WriteJson{T}"/>.
    /// </summary>
    /// <param name="payload">Contenu sérialisé.</param>
    /// <typeparam name="T">Type de la donnée.</typeparam>
    /// <returns>La donnée relue, ou <c>null</c> si le contenu est vide.</returns>
    public static T? ReadJson<T>(byte[] payload)
    {
        return JsonSerializer.Deserialize<T>(payload);
    }

    private static void WriteItem(Stream stream, EntityItemQueryResult item)
    {
        var flags = (byte)((item.EntityNodeProperties != null ? HasNodeProperties : 0)
                           | (item.EntitySnapshot != null ? HasSnapshot : 0)
                           | (item.EntityEdge != null ? HasEdge : 0)
                           | (item.EntityClassProperties != null ? HasClassProperties : 0));
        stream.WriteByte(flags);
        item.EntityNodeProperties?.WriteDelimitedTo(stream);
        item.EntitySnapshot?.WriteDelimitedTo(stream);
        item.EntityEdge?.WriteDelimitedTo(stream);
        item.EntityClassProperties?.WriteDelimitedTo(stream);
    }

    private static EntityItemQueryResult ReadItem(Stream stream)
    {
        var flags = stream.ReadByte();
        if (flags < 0)
        {
            throw new EndOfStreamException("Objet tronqué dans le cache");
        }

        return new EntityItemQueryResult
        {
            EntityNodeProperties = (flags & HasNodeProperties) != 0 ? EntityNodeProperties.Parser.ParseDelimitedFrom(stream) : null,
            EntitySnapshot = (flags & HasSnapshot) != 0 ? EntitySnapshot.Parser.ParseDelimitedFrom(stream) : null,
            EntityEdge = (flags & HasEdge) != 0 ? EntityEdge.Parser.ParseDelimitedFrom(stream) : null,
            EntityClassProperties = (flags & HasClassProperties) != 0 ? EntityClassProperties.Parser.ParseDelimitedFrom(stream) : null
        };
    }
}
