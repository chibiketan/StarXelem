using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Sc.External.Services.Entitlement.V2;

namespace StarXelem.Cli.TestDb;

/// <summary>
/// Sonde pour ExternalEntitlementService.QuerySnapshotsWithEntitlementsStream avec IncludePersistentProperties = true :
/// affiche les variables persistantes de chaque objet du loadout sauvegardé et décode la variable de craft
/// « Crafting.PersistentItemData » (voir Documents/2026-11-23-analyse-objets-craftes.md).
/// </summary>
internal static class SnapshotProbe
{
    private static readonly uint CraftedNameCrc = Crc("crafting.persistentitemdata");

    private static readonly Dictionary<uint, string> KnownNames = new[]
    {
        "crafting.persistentitemdata", "entitylifetimepolicy", "purchasable.persistentitemgameflags", "purchasable.legalownerid", "rsrccntr.map"
    }.ToDictionary(Crc, n => n);

    public static async Task<int> RunAsync(GrpcChannel channel, Metadata headers, string entitlementUrn, string? outFile)
    {
        var client = new ExternalEntitlementService.ExternalEntitlementServiceClient(channel);
        var request = new QuerySnapshotsWithEntitlementsStreamRequest { Filter = new SnapshotFilter(), IncludePersistentProperties = true };
        request.Filter.EntitlementUrn.Add(entitlementUrn);

        var results = new List<SnapshotWithEntitlement>();
        try
        {
            using var call = client.QuerySnapshotsWithEntitlementsStream(request, headers, deadline: DateTime.UtcNow.AddSeconds(60));
            while (await call.ResponseStream.MoveNext(CancellationToken.None))
            {
                results.AddRange(call.ResponseStream.Current.Results);
            }
        }
        catch (RpcException ex)
        {
            Console.Error.WriteLine($"Appel rejeté : {ex.StatusCode} {ex.Status.Detail}");
            return 1;
        }

        if (outFile is not null)
        {
            var formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithIndentation());
            File.WriteAllText(outFile, "[" + string.Join(",\n", results.Select(r => formatter.Format(r))) + "]");
            Console.WriteLine($"Réponse complète écrite dans {outFile}");
        }

        Console.WriteLine($"{results.Count} résultat(s)");
        foreach (var result in results.OrderBy(r => r.Snapshot.SlotId).ThenBy(r => string.Join('/', r.Snapshot.AttachPath)))
        {
            var properties = result.Entitlement?.PersistentProperties;
            Console.WriteLine();
            Console.WriteLine($"[{result.Snapshot.SlotId}] {string.Join('/', result.Snapshot.AttachPath)} : {result.Entitlement?.Metadata?.Name ?? result.Snapshot.ItemUrn}");
            if (properties is null)
            {
                Console.WriteLine("    (aucune propriété persistante)");
                continue;
            }

            foreach (var variable in properties.Variables)
            {
                var name = KnownNames.GetValueOrDefault(variable.NameCrc, "?");
                Console.WriteLine($"    var {variable.NameCrc} ({name}) type={variable.Type} flags={variable.Flags} {variable.Snapshot.Length} o : {Convert.ToHexString(variable.Snapshot.Span)}");
                if (variable.NameCrc == CraftedNameCrc)
                {
                    PrintCrafted(variable.Snapshot.Span);
                }
            }

            foreach (var component in properties.Components)
            {
                Console.WriteLine($"    composant : {component}");
            }
        }

        return 0;
    }

    private static void PrintCrafted(ReadOnlySpan<byte> data)
    {
        try
        {
            var reader = new Reader(data.ToArray());
            var compositionCount = reader.VarInt();
            for (var i = 0UL; i < compositionCount; i++)
            {
                var variant = reader.U8();
                var material = variant <= 1 ? reader.Guid() : "(vide)";
                var quality = reader.U16();
                var amount = reader.I64();
                Console.WriteLine($"      matériau variante={variant} {material} qualité={quality} quantité={amount}");
            }

            Console.WriteLine($"      tier={reader.I8()}");
            var modifierCount = reader.VarInt();
            for (var i = 0UL; i < modifierCount; i++)
            {
                var guid = reader.Guid();
                Console.WriteLine($"      modificateur {guid} = {reader.F32()}");
            }

            Console.WriteLine($"      classId={reader.Guid()} (reste {reader.Remaining} o)");
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            Console.WriteLine($"      décodage interrompu : {ex.Message}");
        }
    }

    private static uint Crc(string name) => Crc32.HashToUInt32(Encoding.ASCII.GetBytes(name.ToLowerInvariant()));

    private sealed class Reader(byte[] data)
    {
        private int _position;

        public int Remaining => data.Length - _position;

        private ReadOnlySpan<byte> Take(int count)
        {
            var span = data.AsSpan(_position, count);
            _position += count;
            return span;
        }

        public byte U8() => Take(1)[0];
        public sbyte I8() => (sbyte)Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

        /// <summary>Les deux uint64 bruts (ordre de lecture du client), pour comparer avec CigGuid.</summary>
        public string Guid()
        {
            var first = BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
            var second = BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
            return $"{first:x16}:{second:x16}";
        }

        public ulong VarInt()
        {
            ulong value = 0;
            var shift = 0;
            while (true)
            {
                var b = U8();
                value |= (ulong)(b & 0x7F) << shift;
                shift += 7;
                if (b < 0x80)
                {
                    return value;
                }
            }
        }
    }
}
