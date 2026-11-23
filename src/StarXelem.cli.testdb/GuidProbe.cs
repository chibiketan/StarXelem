using System.Text.Json;
using System.Text.Json.Serialization;
using StarBreaker.Common;
using StarXelem.Services;

namespace StarXelem.Cli.TestDb;

/// <summary>Sonde : affiche l'enregistrement DataCore de chaque GUID donné (nom, type et contenu JSON).</summary>
internal static class GuidProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 16,
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static async Task RunAsync(IP4kService p4kService, IEnumerable<string> guids, int depth)
    {
        foreach (var guid in guids)
        {
            var record = await p4kService.GetRecordWithSpecificDepth(new CigGuid(guid), depth);
            Console.WriteLine();
            if (record is null)
            {
                Console.WriteLine($"--- {guid} : introuvable");
                continue;
            }

            Console.WriteLine($"--- {guid} : {record.RecordName} ({record.Data?.GetType().Name})");
            try
            {
                Console.WriteLine(JsonSerializer.Serialize(record.Data, record.Data?.GetType() ?? typeof(object), JsonOptions));
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                Console.WriteLine($"(sérialisation impossible : {ex.Message})");
            }
        }
    }
}
