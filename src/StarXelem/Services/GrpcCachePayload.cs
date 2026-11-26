using System.Text.Json;

using Google.Protobuf;
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
        int flags;
        while ((flags = stream.ReadByte()) >= 0)
        {
            items.Add(new EntityItemQueryResult
            {
                EntityNodeProperties = (flags & HasNodeProperties) != 0 ? EntityNodeProperties.Parser.ParseDelimitedFrom(stream) : null,
                EntitySnapshot = (flags & HasSnapshot) != 0 ? EntitySnapshot.Parser.ParseDelimitedFrom(stream) : null,
                EntityEdge = (flags & HasEdge) != 0 ? EntityEdge.Parser.ParseDelimitedFrom(stream) : null,
                EntityClassProperties = (flags & HasClassProperties) != 0 ? EntityClassProperties.Parser.ParseDelimitedFrom(stream) : null
            });
        }

        return items;
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
}
